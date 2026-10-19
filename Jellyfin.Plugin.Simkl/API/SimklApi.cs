using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// The Simkl HTTP API.
    /// </summary>
    /// <remarks>
    /// Two client ids, not one. Simkl binds every access token to the app it was issued under, so a token minted by
    /// the old pin flow stops resolving the moment a request names the OAuth 2 client id, and the user looks logged
    /// out. Every call therefore takes its client id from the token it carries: a simkl_at_ token goes out under
    /// <see cref="ApikeyOAuth2"/> and anything else under <see cref="ApikeyLegacy"/>. Only the /oauth2 routes pick
    /// one outright, and they always pick the new one.
    /// </remarks>
    public class SimklApi
    {
        /// <summary>
        /// Base url.
        /// </summary>
        public const string Baseurl = @"https://api.simkl.com";

        /// <summary>
        /// The client id of the app the old pin flow issued tokens under. Kept for exactly that: those tokens never
        /// expire and keep working, and they only work under this id.
        /// </summary>
        public const string ApikeyLegacy = @"c721b22482097722a84a20ccc579cf9d232be85b9befe7b7805484d0ddbc6781";

        /// <summary>
        /// The client id of the OAuth 2 app every new sign in uses. A public device client, so it has no secret;
        /// it is also bearer only, which is why even the file search now travels with the user's token.
        /// </summary>
        public const string ApikeyOAuth2 = @"868dc2dcf0ce6e5a2feca8c0b2f3fbfa92ea30d20ec7983036cb053196b2e136";

        /// <summary>
        /// The scope a device login asks for. Reading is not enough: a scrobble is a write, and so is the file
        /// search, which the API classifies by "POST with a body" rather than by path.
        /// </summary>
        public const string Scope = "media:read media:write";

        /// <summary>
        /// The app name Simkl knows this plugin by.
        /// </summary>
        public const string AppName = "jellyfin-simkl";

        /// <summary>
        /// How long Simkl grants an access token, in seconds. Only used if an answer ever omits expires_in.
        /// </summary>
        public const int DefaultAccessLifetimeSeconds = 604800;

        private const int LoggedBodyLength = 500;

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        // how long a read of the Simkl account stands in for the next one
        private static readonly TimeSpan AccountTtl = TimeSpan.FromMinutes(10);

        // the whole history can take minutes to build on Simkl's side
        private static readonly TimeSpan AllItemsTimeout = TimeSpan.FromSeconds(300);

        private readonly ILogger<SimklApi> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly JsonSerializerOptions _caseInsensitiveJsonSerializerOptions;
        private readonly string _appVersion;
        private readonly string _clientParamsLegacy;
        private readonly string _clientParamsOAuth2;

        // One renewal at a time per refresh token, not per user: two Jellyfin users may have signed in to the same
        // Simkl account and share a token, and two renewals of one grant would leave the loser holding an access
        // token the winner already superseded. Never emptied; there is one entry per signed in Simkl account.
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        // the account behind each token, so the plugin page reading it on open and after every save is one call
        private readonly ConcurrentDictionary<string, (DateTime ReadAt, UserSettingsResponse Settings)> _accounts = new ConcurrentDictionary<string, (DateTime, UserSettingsResponse)>(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApi"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SimklApi}"/> interface.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        /// <param name="appHost">Instance of the <see cref="IServerApplicationHost"/> interface.</param>
        public SimklApi(ILogger<SimklApi> logger, IHttpClientFactory httpClientFactory, IServerApplicationHost appHost)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _appVersion = typeof(SimklApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

            // every request carries client_id, app-name, app-version and the server version as query params,
            // and a name/version User-Agent; Authorization only where a user token is involved
            var shared = "&app-name=" + AppName
                + "&app-version=" + _appVersion
                + "&jellyfin-version=" + Uri.EscapeDataString(appHost.ApplicationVersionString);
            _clientParamsLegacy = "client_id=" + ApikeyLegacy + shared;
            _clientParamsOAuth2 = "client_id=" + ApikeyOAuth2 + shared;

            _jsonSerializerOptions = new JsonSerializerOptions(JsonDefaults.Options)
            {
                // Simkl reads a key it is sent: a null movie or show must not be in the body at all
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            // An id or a season number arrives quoted for one title and bare for the next. Nothing extra is needed for
            // that: JsonDefaults.Options already sets NumberHandling.AllowReadingFromString and registers Jellyfin's
            // own JsonStringConverter, and System.Text.Json takes the first matching converter in the list.
            _caseInsensitiveJsonSerializerOptions = new JsonSerializerOptions(JsonDefaults.Options)
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Starts a device login: Simkl mints a code the user types on simkl.com and one the plugin polls with.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The device authorization, or null when Simkl refused to start one.</returns>
        public async Task<DeviceCodeResponse?> StartDeviceLogin(CancellationToken cancellationToken)
        {
            var body = new OAuthRequest { ClientId = ApikeyOAuth2, Scope = Scope };
            var (status, payload, _) = await Send(HttpMethod.Post, "/oauth2/device", null, body, DefaultTimeout, cancellationToken, ApikeyOAuth2).ConfigureAwait(false);
            if (status != HttpStatusCode.OK)
            {
                _logger.LogError("Simkl wouldn't start a device login: HTTP {Status} {Error}", (int)status, ErrorFrom(payload));
                return null;
            }

            return Deserialize<DeviceCodeResponse>(payload);
        }

        /// <summary>
        /// Asks whether a device code has been approved yet.
        /// </summary>
        /// <param name="deviceCode">The device code the login was started with.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The tokens once it was approved, otherwise the RFC 6749 error saying why not.</returns>
        public Task<(TokenResponse? Tokens, string? Error)> PollDeviceLogin(string deviceCode, CancellationToken cancellationToken)
        {
            var body = new OAuthRequest
            {
                ClientId = ApikeyOAuth2,
                GrantType = OAuthRequest.DeviceCodeGrant,
                DeviceCode = deviceCode
            };

            // quiet: every poll before the user approves is answered with a 400, and those are not failures
            return Exchange(body, true, cancellationToken);
        }

        /// <summary>
        /// Builds the scrobble body of what is playing, from the ids the library has.
        /// </summary>
        /// <remarks>
        /// Simkl identifies the show from show.ids, falling back to title and year, and the episode from its season
        /// and number. The series ids therefore belong on the show: sent as episode ids they make the lookup fail.
        /// An ids object that resolved nothing at all is left out entirely so the title and year are used instead.
        /// </remarks>
        /// <param name="item">What is playing. For an episode this is the episode, carrying its own provider ids
        /// and the production year of the series.</param>
        /// <param name="progress">How much of it has been watched, 0 to 100.</param>
        /// <param name="seriesProviderIds">The provider ids of the parent series, for an episode.</param>
        /// <returns>The request, or null when the item is neither a movie nor an episode.</returns>
        public static ScrobbleRequest? BuildRequest(BaseItemDto item, float progress, IReadOnlyDictionary<string, string>? seriesProviderIds)
        {
            ArgumentNullException.ThrowIfNull(item);
            var request = new ScrobbleRequest { Progress = progress };
            var itemProviderIds = item.ProviderIds ?? new Dictionary<string, string>();

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                request.Movie = new ScrobbleMovie
                {
                    // OriginalTitle is empty for a lot of libraries, and a null title leaves Simkl nothing to match
                    // on when there are no usable ids either
                    Title = string.IsNullOrEmpty(item.OriginalTitle) ? item.Name : item.OriginalTitle,
                    Year = item.ProductionYear,
                    Ids = NullIfEmpty(new SimklMovieIds(itemProviderIds))
                };
                return request;
            }

            if (item.Type == BaseItemKind.Episode)
            {
                request.Show = new ScrobbleShow
                {
                    Title = item.SeriesName,
                    Year = item.ProductionYear,
                    Ids = NullIfEmpty(new SimklShowIds(seriesProviderIds ?? new Dictionary<string, string>()))
                };
                request.Episode = new ScrobbleEpisode
                {
                    Season = item.ParentIndexNumber,
                    Number = item.IndexNumber,
                    Ids = NullIfEmpty(new SimklEpisodeIds(itemProviderIds))
                };
                return request;
            }

            return null;
        }

        // an ids object that resolved no ids at all is dropped, so Simkl falls back to matching on title and year
        private static T? NullIfEmpty<T>(T ids)
            where T : SimklIds
        {
            return ids.HasAnyId() ? ids : null;
        }

        // carries what Simkl answered with, deserialized into the base shape, over to the one a request asks for
        private static T? Carry<T>(SimklIds? ids)
            where T : SimklIds, new()
        {
            if (ids == null)
            {
                return null;
            }

            var carried = new T();
            ids.CopyTo(carried);
            return NullIfEmpty(carried);
        }

        /// <summary>
        /// Fallback when Simkl cannot match the ids: identify the file by name and build the request from Simkl's
        /// own match.
        /// </summary>
        /// <param name="dto">The item dto of what is playing.</param>
        /// <param name="fullpath">True to search by the whole path, false for the file name alone.</param>
        /// <param name="progress">How much of it has been watched, 0 to 100.</param>
        /// <param name="userConfig">The settings of the user watching, whose token the search travels with.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The request, or null when the item has no path or the search found nothing of its type.</returns>
        public async Task<ScrobbleRequest?> BuildRequestFromFile(BaseItemDto dto, bool fullpath, float progress, UserConfig userConfig, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentNullException.ThrowIfNull(userConfig);
            var name = fullpath ? dto.Path : Path.GetFileName(dto.Path);
            var found = string.IsNullOrEmpty(name) ? null : await GetFromFile(name, userConfig, cancellationToken).ConfigureAwait(false);
            var request = new ScrobbleRequest { Progress = progress };

            if (dto.Type == BaseItemKind.Movie && found?.Movie != null && string.Equals(found.Type, "movie", StringComparison.OrdinalIgnoreCase))
            {
                request.Movie = new ScrobbleMovie { Title = found.Movie.Title, Year = found.Movie.Year, Ids = Carry<SimklMovieIds>(found.Movie.Ids) };
                return request;
            }

            if (dto.Type == BaseItemKind.Episode && found?.Show != null && found.Episode != null && string.Equals(found.Type, "episode", StringComparison.OrdinalIgnoreCase))
            {
                request.Show = new ScrobbleShow { Title = found.Show.Title, Year = found.Show.Year, Ids = Carry<SimklShowIds>(found.Show.Ids) };
                // Simkl identified the file itself, so its own season and episode numbers are authoritative here
                request.Episode = new ScrobbleEpisode { Season = found.Episode.Season, Number = found.Episode.Episode };
                return request;
            }

            _logger.LogInformation("Simkl file search returned {Found} for {Type} {Name}", found?.Type ?? "nothing", dto.Type, name);
            return null;
        }

        /// <summary>
        /// Calls /scrobble/start, to report that playback has begun or resumed.
        /// </summary>
        /// <param name="request">What is playing and how far it is.</param>
        /// <param name="userConfig">The settings of the user watching.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>What Simkl answered.</returns>
        public Task<(HttpStatusCode Status, ScrobbleResponse? Response, TimeSpan? RetryAfter)> ScrobbleStart(ScrobbleRequest request, UserConfig userConfig, CancellationToken cancellationToken)
        {
            return Scrobble("start", request, userConfig, cancellationToken);
        }

        /// <summary>
        /// Reports one playback event.
        /// </summary>
        /// <remarks>
        /// This is the only call that hands the caller the status code instead of throwing on it: the scrobbler
        /// treats 404 (identify the file by name instead), 409 (already watched within the hour) and the rate limit
        /// each as a normal outcome of its own, and it needs the Retry-After to decide how long to hold off. On a
        /// stop, Simkl's own clock decides what counts as watched, at 80%; below that the position is kept instead.
        /// </remarks>
        /// <param name="action">start, pause or stop.</param>
        /// <param name="request">What is playing and how far it is.</param>
        /// <param name="userConfig">The settings of the user watching.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The status Simkl answered with, the answer when there was a body worth reading, and how long it
        /// asked to be left alone for.</returns>
        /// <exception cref="InvalidTokenException">Simkl rejected the token, which has been cleared.</exception>
        public async Task<(HttpStatusCode Status, ScrobbleResponse? Response, TimeSpan? RetryAfter)> Scrobble(string action, ScrobbleRequest request, UserConfig userConfig, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(userConfig);

            // the ids travel at Information, not only in the Debug body dump: they are what a "Simkl matched the
            // wrong title" report is diagnosed from
            _logger.LogInformation("Simkl scrobble {Action} at {Progress}% for {Subject}", action, request.Progress, Describe(request));

            await EnsureFresh(userConfig, cancellationToken).ConfigureAwait(false);

            // rewatches are opted into per user and only make sense on the stop, Simkl ignores the flag on a start
            var url = "/scrobble/" + action
                + (string.Equals(action, "stop", StringComparison.Ordinal) && userConfig.AllowRewatch ? "?allow_rewatch=yes" : string.Empty);

            // what this attempt goes out with, so a rejection can be attributed to the right token
            var sent = userConfig.UserToken;
            var (status, payload, retryAfter) = await Send(HttpMethod.Post, url, sent, request, DefaultTimeout, cancellationToken, quiet: true).ConfigureAwait(false);
            if (status == HttpStatusCode.Unauthorized)
            {
                // An access token Simkl stopped accepting earlier than its stored expiry is worth one renewal before
                // the login is given up on - unless a renewal already replaced it while this call was in flight, in
                // which case the rejection is of a token nobody holds any more and going again is enough. Replaced
                // by nothing is not replaced: a logout landing mid request empties these settings, and going again
                // then would send the call with no credential at all.
                var superseded = !string.Equals(userConfig.UserToken, sent, StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(userConfig.UserToken);
                if (superseded || await TryRefresh(userConfig, true, cancellationToken).ConfigureAwait(false))
                {
                    sent = userConfig.UserToken;
                    (status, payload, retryAfter) = await Send(HttpMethod.Post, url, sent, request, DefaultTimeout, cancellationToken, quiet: true).ConfigureAwait(false);
                }

                if (status == HttpStatusCode.Unauthorized)
                {
                    throw InvalidateToken(userConfig, sent);
                }
            }

            // 404, 409 and the rate limit each carry no body the scrobbler needs; it acts on the status alone and
            // reports each as the outcome it is. Anything else is shown here with its body, which the scrobbler
            // never sees.
            if ((int)status >= 400)
            {
                if (status != HttpStatusCode.NotFound && status != HttpStatusCode.Conflict && status != HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Simkl answered HTTP {Status} for scrobble {Action}: {Body}", (int)status, action, Excerpt(payload));
                }

                return (status, null, retryAfter);
            }

            // an HTTP 200 can still carry an error field; the scrobbler reports it, since only it holds the item the
            // call was about
            return (status, Deserialize<ScrobbleResponse>(payload), retryAfter);
        }

        /// <summary>
        /// Adds what a user marked as watched by hand. Never with allow_rewatch, so re-marking something already
        /// watched stays a no-op.
        /// </summary>
        /// <param name="request">The movies and episodes that were marked.</param>
        /// <param name="userConfig">The settings of the user who marked them.</param>
        /// <returns>What Simkl added, and what it could not identify.</returns>
        public Task<SyncHistoryResponse?> AddToHistory(HistoryRequest request, UserConfig userConfig)
        {
            return Authorized(userConfig, () => Post<SyncHistoryResponse>("/sync/history", userConfig.UserToken, request));
        }

        /// <summary>
        /// Removes a single item a user unmarked by hand. Every show entry names its episodes, one without them
        /// would wipe the whole show.
        /// </summary>
        /// <param name="request">The movies and episodes that were unmarked.</param>
        /// <param name="userConfig">The settings of the user who unmarked them.</param>
        /// <returns>What Simkl removed, and what it could not identify.</returns>
        public Task<SyncHistoryResponse?> RemoveFromHistory(HistoryRequest request, UserConfig userConfig)
        {
            return Authorized(userConfig, () => Post<SyncHistoryResponse>("/sync/history/remove", userConfig.UserToken, request));
        }

        /// <summary>
        /// Reads a whole Simkl library, or everything that changed since a date.
        /// </summary>
        /// <param name="query">The query string of the call, without the leading question mark.</param>
        /// <param name="userConfig">The settings of the user whose library is read.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The entries, never null: an empty library answers the literal null for some client ids.</returns>
        public Task<AllItemsResponse> GetAllItems(string query, UserConfig userConfig, CancellationToken cancellationToken)
        {
            return Authorized(
                userConfig,
                async () => await Get<AllItemsResponse>("/sync/all-items?" + query, userConfig.UserToken, AllItemsTimeout, cancellationToken).ConfigureAwait(false)
                    ?? new AllItemsResponse());
        }

        /// <summary>
        /// Reads the Simkl account behind a user token, from a short lived cache when it was read recently.
        /// </summary>
        /// <remarks>
        /// The plugin page reads this when it opens and again after every save, so a few clicks on the checkboxes
        /// used to be a call to Simkl each. What it returns is a display name and a plan, which change about as
        /// often as the user edits their Simkl profile, so one entry per token for a few minutes collapses all of
        /// that into one call. Keyed by the token, so a renewal or a fresh login reads through by itself; a plan
        /// bought on simkl.com mid-session can be up to the cache lifetime late in showing here.
        /// </remarks>
        /// <param name="userConfig">The settings of the user whose account is read.</param>
        /// <returns>The account and the user name.</returns>
        public async Task<UserSettingsResponse?> GetUserSettings(UserConfig userConfig)
        {
            ArgumentNullException.ThrowIfNull(userConfig);
            if (_accounts.TryGetValue(userConfig.UserToken, out var cached) && DateTime.UtcNow - cached.ReadAt < AccountTtl)
            {
                return cached.Settings;
            }

            var settings = await Authorized(userConfig, () => Post<UserSettingsResponse>("/users/settings", userConfig.UserToken)).ConfigureAwait(false);

            // under the token the answer actually came back for, which is not the one this started with when the
            // call renewed it on the way through. An unusable answer is not cached, so it is asked again next time.
            if (settings != null)
            {
                _accounts[userConfig.UserToken] = (DateTime.UtcNow, settings);
                PruneAccounts();
            }

            return settings;
        }

        /// <summary>
        /// Reads when each Simkl list of a user last changed.
        /// </summary>
        /// <param name="userConfig">The settings of the user whose activity is read.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The activity timestamps.</returns>
        public Task<ActivitiesResponse?> GetActivities(UserConfig userConfig, CancellationToken cancellationToken)
        {
            return Authorized(userConfig, () => Get<ActivitiesResponse>("/sync/activities", userConfig.UserToken, DefaultTimeout, cancellationToken));
        }

        // a token that is renewed weekly would otherwise leave an entry behind every time
        private void PruneAccounts()
        {
            var now = DateTime.UtcNow;
            foreach (var entry in _accounts)
            {
                if (now - entry.Value.ReadAt >= AccountTtl)
                {
                    _accounts.TryRemove(entry.Key, out _);
                }
            }
        }

        // null when Simkl found nothing: that answer is an empty array, not an object. The user's own bearer goes
        // with it - the OAuth 2 app is bearer only, and the file search is not one of the catalog paths exempt
        // from that, so an anonymous search would be refused outright.
        private Task<SearchFileResponse?> GetFromFile(string filename, UserConfig userConfig, CancellationToken cancellationToken)
        {
            return Authorized(userConfig, () => Post<SearchFileResponse>("/search/file/", userConfig.UserToken, new SimklFile { File = filename }, cancellationToken));
        }

        // runs one call made with the user's token, renewing it first when it is nearly out and once more if Simkl
        // rejects it anyway; clears the token when even that does not help
        private async Task<T> Authorized<T>(UserConfig userConfig, Func<Task<T>> call)
        {
            await EnsureFresh(userConfig, CancellationToken.None).ConfigureAwait(false);

            // what this attempt actually goes out with, so a rejection can be attributed to the right token
            var sent = userConfig.UserToken;
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                // A renewal that landed while this call was in flight has already replaced the token it carried,
                // and Simkl drops the old one the moment it does, so the rejection is of a token that is no longer
                // anyone's. Retrying is enough; renewing again would mint a third token for nothing.
                if (string.Equals(userConfig.UserToken, sent, StringComparison.Ordinal)
                    && !await TryRefresh(userConfig, true, CancellationToken.None).ConfigureAwait(false))
                {
                    throw InvalidateToken(userConfig, sent);
                }
            }

            // the call reads the token off the settings when it runs, so the retry goes out with the new one
            var retried = userConfig.UserToken;
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw InvalidateToken(userConfig, retried);
            }
        }

        private Task EnsureFresh(UserConfig userConfig, CancellationToken cancellationToken)
        {
            return userConfig.NeedsRefresh ? TryRefresh(userConfig, false, cancellationToken) : Task.CompletedTask;
        }

        // Renews the access token behind these settings. force skips the "is it nearly out" test, which is what a
        // 401 on a token that still looks fresh needs. False means the login is over: either there is nothing to
        // renew with (an old pin flow token) or Simkl refused the refresh token.
        private async Task<bool> TryRefresh(UserConfig userConfig, bool force, CancellationToken cancellationToken)
        {
            var refresh = userConfig.RefreshToken;
            if (!userConfig.IsOAuth2 || string.IsNullOrEmpty(refresh))
            {
                return false;
            }

            // read before queueing on the gate: seeing this token change while waiting is how a caller knows the
            // holder renewed on its behalf and it must not renew again
            var attempted = userConfig.UserToken;
            var gate = _refreshGates.GetOrAdd(refresh, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AdoptStored(userConfig);
                if (!string.Equals(userConfig.UserToken, attempted, StringComparison.Ordinal))
                {
                    return !string.IsNullOrEmpty(userConfig.UserToken);
                }

                if (!force && !userConfig.NeedsRefresh)
                {
                    return true;
                }

                var body = new OAuthRequest
                {
                    ClientId = ApikeyOAuth2,
                    GrantType = OAuthRequest.RefreshTokenGrant,
                    RefreshToken = refresh
                };

                var (tokens, error) = await Exchange(body, false, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(tokens?.AccessToken))
                {
                    _logger.LogError("Couldn't renew the Simkl access token: {Error}", error ?? "Simkl answered nothing usable");

                    // Only Simkl refusing this grant ends the login. A 502, a rate limit or anything else that is
                    // not an answer about the refresh token says nothing about it, and answering false to a forced
                    // renewal has the caller throw a still valid login away. Those are failed requests, and reach
                    // the caller the same way a dropped connection already does.
                    if (force && !OAuthErrorResponse.IsTerminal(error))
                    {
                        throw new HttpRequestException("Simkl could not renew the access token: " + error);
                    }

                    return false;
                }

                var expiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn ?? DefaultAccessLifetimeSeconds);
                // Simkl does not rotate the refresh token, but an answer that ever carries one wins
                var kept = string.IsNullOrEmpty(tokens.RefreshToken) ? refresh : tokens.RefreshToken;
                var scope = string.IsNullOrEmpty(tokens.Scope) ? userConfig.TokenScope : tokens.Scope;

                // Simkl took seconds to answer, and a logout or a sign in to another account may have landed in the
                // meantime. ReplaceToken matches on the token that was renewed, so it writes nothing when one did
                // and says as much, and neither must this: these settings are usually the stored object itself, so
                // assigning anyway would put the abandoned login back, holding a token that is now freshly valid.
                if (SimklPlugin.Instance?.Configuration.ReplaceToken(attempted, tokens.AccessToken, kept, expiresAt, scope) == false)
                {
                    _logger.LogInformation("Dropped a renewed Simkl access token: the login it belonged to is gone");

                    // and take over whatever replaced it, so a caller holding a snapshot stops using the old one
                    AdoptStored(userConfig);
                    return !string.IsNullOrEmpty(userConfig.UserToken);
                }

                // ReplaceToken has already written these when the settings are the stored object; a caller holding
                // a detached copy of them needs them written here
                userConfig.UserToken = tokens.AccessToken;
                userConfig.RefreshToken = kept;
                userConfig.TokenExpiresAt = expiresAt;
                userConfig.TokenScope = scope;

                _logger.LogInformation("Renewed the Simkl access token, good until {Expiry:u}", expiresAt);
                return true;
            }
            finally
            {
                gate.Release();
            }
        }

        // takes over a login that was replaced while this snapshot was in hand, so a caller holding a copy rather
        // than the stored object does not renew a token that is already gone
        private static void AdoptStored(UserConfig userConfig)
        {
            var stored = SimklPlugin.Instance?.Configuration.GetByGuid(userConfig.Id);
            if (stored != null && !ReferenceEquals(stored, userConfig))
            {
                userConfig.CopyTokensFrom(stored);
            }
        }

        private async Task<(TokenResponse? Tokens, string? Error)> Exchange(OAuthRequest body, bool quiet, CancellationToken cancellationToken)
        {
            var (status, payload, _) = await Send(HttpMethod.Post, "/oauth2/token", null, body, DefaultTimeout, cancellationToken, ApikeyOAuth2, quiet).ConfigureAwait(false);
            if (status == HttpStatusCode.OK)
            {
                return (Deserialize<TokenResponse>(payload), null);
            }

            return (null, ErrorFrom(payload)
                ?? "HTTP " + ((int)status).ToString(CultureInfo.InvariantCulture));
        }

        // rejected is the token the failed request actually carried, which is not always the one on the settings by
        // the time the answer arrives: a renewal may have replaced it in between, and clearing that one would throw
        // away a login Simkl never refused
        private InvalidTokenException InvalidateToken(UserConfig userConfig, string rejected)
        {
            _logger.LogError("Invalid Simkl user token, deleting it");
            _accounts.TryRemove(rejected, out _);

            // Clear it by VALUE, through the locked helper: the caller may hold a snapshot the page has since
            // replaced, and clearing by value means only the token Simkl actually rejected is removed. Writing the
            // stored object directly here would also save the whole configuration outside SaveLock and could drop a
            // concurrent page save.
            SimklPlugin.Instance?.Configuration.DeleteUserToken(rejected);

            // and only clear the snapshot when it is still holding that same token
            if (string.Equals(userConfig.UserToken, rejected, StringComparison.Ordinal))
            {
                userConfig.ClearTokens();
            }

            return new InvalidTokenException("Invalid user token");
        }

        private Task<T?> Get<T>(string url, string? userToken = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            return SendAsync<T>(HttpMethod.Get, url, userToken, null, timeout ?? DefaultTimeout, cancellationToken);
        }

        private Task<T?> Post<T>(string url, string? userToken = null, object? data = null, CancellationToken cancellationToken = default)
        {
            return SendAsync<T>(HttpMethod.Post, url, userToken, data, DefaultTimeout, cancellationToken);
        }

        // Turns an answer into the object it carries, or into an exception. It logs nothing: Send has already
        // reported this exact answer, and logging it again here put every single call in the log twice.
        private async Task<T?> SendAsync<T>(HttpMethod method, string url, string? userToken, object? data, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var (status, payload, _) = await Send(method, url, userToken, data, timeout, cancellationToken).ConfigureAwait(false);
            if ((int)status >= 400)
            {
                throw new HttpRequestException(
                    string.Format(CultureInfo.InvariantCulture, "Simkl answered HTTP {0}", (int)status),
                    null,
                    status);
            }

            return Deserialize<T>(payload);
        }

        // Retry-After is either a number of seconds or an HTTP date
        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter?.Delta != null)
            {
                return retryAfter.Delta;
            }

            if (retryAfter?.Date == null)
            {
                return null;
            }

            var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        // clientId overrides the client the call goes out as; without one it follows the token, which is what keeps
        // an old pin flow token on the app it was issued under. quiet is for calls whose refusals the caller reports
        // itself: the device poll, whose 400s are the normal course of the flow, and the scrobble.
        private async Task<(HttpStatusCode Status, string Payload, TimeSpan? RetryAfter)> Send(
            HttpMethod method,
            string url,
            string? userToken,
            object? data,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string? clientId = null,
            bool quiet = false)
        {
            var apiKey = clientId ?? (UserConfig.IsOAuth2Token(userToken) ? ApikeyOAuth2 : ApikeyLegacy);
            var clientParams = string.Equals(apiKey, ApikeyOAuth2, StringComparison.Ordinal) ? _clientParamsOAuth2 : _clientParamsLegacy;

            using var request = new HttpRequestMessage(method, new Uri(Baseurl + WithClientParams(url, clientParams)));
            request.Headers.TryAddWithoutValidation("simkl-api-key", apiKey);
            request.Headers.UserAgent.ParseAdd(AppName + "/" + _appVersion);
            if (!string.IsNullOrEmpty(userToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            }

            // a bodyless POST still declares JSON, the way Emby's unconditional RequestContentType did: /users/settings
            // is posted with no body and Simkl rejects it without the content type
            var body = data == null
                ? (method == HttpMethod.Post ? string.Empty : null)
                : JsonSerializer.Serialize(data, _jsonSerializerOptions);
            if (body != null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json);
            }

            // Nothing of an /oauth2 exchange is ever written to a log: the request body carries the device code and
            // the refresh token, and a successful answer carries both tokens. Debug logging is a support request
            // away from being switched on, and these are bearer credentials.
            var secret = url.StartsWith("/oauth2/", StringComparison.Ordinal);

            // what was actually sent, so a title Simkl matched to the wrong entry can still be diagnosed from a log
            if (!string.IsNullOrEmpty(body) && !secret)
            {
                _logger.LogDebug("Simkl {Method} {Url} body: {Body}", method.Method, url, body);
            }

            // a fresh HttpClient wrapping the pooled handler, so the long all-items pull never shortens or
            // stretches the timeout of anything else
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            client.Timeout = timeout;

            string payload;
            HttpStatusCode status;
            TimeSpan? retryAfter;
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                retryAfter = GetRetryAfter(response);
                payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // the timeout of the call itself, which has to reach the caller as a failed request, not as a cancellation
                throw new HttpRequestException(
                    string.Format(CultureInfo.InvariantCulture, "Simkl did not answer within {0} seconds", timeout.TotalSeconds),
                    ex);
            }

            // a refusal from /oauth2 is the RFC error envelope and holds no credential, but a success there is the
            // grant itself, so only the refusals of those routes are ever shown
            var logged = secret && (int)status < 400 ? "[tokens]" : payload;
            if ((int)status >= 400 && !quiet)
            {
                // an error body is never truncated: the not_found list and the reason are exactly what is needed
                _logger.LogError("Simkl answered HTTP {Status} for {Method} {Url}: {Body}", (int)status, method.Method, url, logged);
            }
            else
            {
                _logger.LogDebug("Simkl answered HTTP {Status} for {Method} {Url}: {Body}", (int)status, method.Method, url, Excerpt(logged));
            }

            return (status, payload, retryAfter);
        }

        private static string WithClientParams(string url, string clientParams)
        {
            return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + clientParams;
        }

        // The reason an /oauth2 call was refused, when the answer carries one. Never throws: a refusal body is
        // whatever the thing standing in front of Simkl felt like sending, an HTML error page as readily as the RFC
        // envelope, and the callers want the reason rather than an exception. Without this a 502 during a token
        // renewal reached the scrobbler and the importer as a JsonException.
        private string? ErrorFrom(string payload)
        {
            try
            {
                return Deserialize<OAuthErrorResponse>(payload)?.Error;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // an answer that carries nothing usable: an empty body, the literal null an empty library answers, and the
        // empty array the file search answers when it recognized nothing
        private T? Deserialize<T>(string payload)
        {
            var body = payload.Trim();
            if (body.Length == 0 || string.Equals(body, "null", StringComparison.Ordinal) || body[0] == '[')
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(body, _caseInsensitiveJsonSerializerOptions);
        }

        // "Blade Runner (1982) [imdb=tt0083658 tmdb=78]" or "Dune S1E3 [tvdb=12345]"
        private static string Describe(ScrobbleRequest request)
        {
            if (request.Movie != null)
            {
                return request.Movie.Title + " (" + request.Movie.Year + ") " + Describe(request.Movie.Ids);
            }

            return request.Show?.Title
                + " S" + request.Episode?.Season + "E" + request.Episode?.Number
                + " " + Describe(request.Show?.Ids);
        }

        private static string Describe(SimklIds? ids)
        {
            if (ids == null)
            {
                return "[no ids]";
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(ids.Imdb))
            {
                parts.Add("imdb=" + ids.Imdb);
            }

            if (!string.IsNullOrEmpty(ids.Tmdb))
            {
                parts.Add("tmdb=" + ids.Tmdb);
            }

            if (!string.IsNullOrEmpty(ids.Tvdb))
            {
                parts.Add("tvdb=" + ids.Tvdb);
            }

            if (ids.Anidb.HasValue)
            {
                parts.Add("anidb=" + ids.Anidb.Value.ToString(CultureInfo.InvariantCulture));
            }

            return parts.Count == 0 ? "[no ids]" : "[" + string.Join(' ', parts) + "]";
        }

        private static string Excerpt(string payload)
        {
            return payload.Length <= LoggedBodyLength ? payload : payload[..LoggedBodyLength];
        }
    }
}
