using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Simkl Api.
    /// </summary>
    public class SimklApi
    {
        /* BASIC API THINGS */

        /// <summary>
        /// Base url.
        /// </summary>
        private const string Baseurl = @"https://api.simkl.com";

        /// <summary>
        /// Redirect uri.
        /// </summary>
        private const string RedirectUri = @"https://simkl.com/apps/jellyfin/connected/";

        /// <summary>
        /// Api key.
        /// </summary>
        private const string Apikey = @"c721b22482097722a84a20ccc579cf9d232be85b9befe7b7805484d0ddbc6781";

        /// <summary>
        /// Secret.
        /// </summary>
        private const string Secret = @"87893fc73cdbd2e51a7c63975c6f941ac1c6155c0e20ffa76b83202dd10a507e";

        /// <summary>
        /// Response bodies are logged verbatim on failure; cap them so a stray HTML error page
        /// cannot flood the log.
        /// </summary>
        private const int MaxLoggedBodyLength = 2048;

        /* INTERFACES */
        private readonly ILogger<SimklApi> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly JsonSerializerOptions _caseInsensitiveJsonSerializerOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApi"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SimklApi}"/> interface.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        public SimklApi(ILogger<SimklApi> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _jsonSerializerOptions = JsonDefaults.Options;
            _caseInsensitiveJsonSerializerOptions = new JsonSerializerOptions(_jsonSerializerOptions)
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Get code.
        /// </summary>
        /// <returns>Code response.</returns>
        public async Task<CodeResponse?> GetCode()
        {
            var uri = $"/oauth/pin?client_id={Apikey}&redirect={RedirectUri}";
            return await Get<CodeResponse>(uri).ConfigureAwait(false);
        }

        /// <summary>
        /// Get code status.
        /// </summary>
        /// <param name="userCode">User code.</param>
        /// <returns>Code status.</returns>
        public async Task<CodeStatusResponse?> GetCodeStatus(string userCode)
        {
            var uri = $"/oauth/pin/{userCode}?client_id={Apikey}";
            return await Get<CodeStatusResponse>(uri).ConfigureAwait(false);
        }

        /// <summary>
        /// Get user settings.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns>User settings.</returns>
        public async Task<UserSettings?> GetUserSettings(string userToken)
        {
            try
            {
                return await Post<UserSettings, object>("/users/settings/", userToken).ConfigureAwait(false);
            }
            catch (InvalidTokenException)
            {
                // Wontfix: Custom status codes
                // "You don't get to pick your response code" - Luke (System Architect of Emby)
                // https://emby.media/community/index.php?/topic/61889-wiki-issue-resultfactorythrowerror/
                return new UserSettings { Error = "user_token_failed" };
            }
        }

        /// <summary>
        /// Mark as watched.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>Status.</returns>
        public async Task<(bool Success, BaseItemDto Item)> MarkAsWatched(BaseItemDto item, string userToken)
        {
            var history = CreateHistoryFromItem(item);
            var r = await SyncHistoryAsync(history, userToken).ConfigureAwait(false);
            _logger.LogDebug("BaseItem: {@Item}", item);
            _logger.LogDebug("History: {@History}", history);
            _logger.LogDebug("Response: {@Response}", r);
            if (r != null && history.Movies.Count == r.Added.Movies
                          && history.Shows.Count == r.Added.Shows
                          && history.Episodes.Count == r.Added.Episodes)
            {
                return (true, item);
            }

            // If we are here, is because the item has not been found
            // let's try scrobbling from full path
            try
            {
                (history, item) = await GetHistoryFromFileName(item).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                // Let's try again but this time using only the FILE name
                _logger.LogDebug("Couldn't scrobble using full path, trying using only filename");
                (history, item) = await GetHistoryFromFileName(item, false).ConfigureAwait(false);
            }

            r = await SyncHistoryAsync(history, userToken).ConfigureAwait(false);
            return r == null
                ? (false, item)
                : (history.Movies.Count == r.Added.Movies && history.Shows.Count == r.Added.Shows, item);
        }

        /// <summary>
        /// Get from file.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <returns>Search file response.</returns>
        private async Task<SearchFileResponse?> GetFromFile(string filename)
        {
            var f = new SimklFile { File = filename };
            _logger.LogInformation("Posting: {@File}", f);
            return await Post<SearchFileResponse, SimklFile>("/search/file/", null, f).ConfigureAwait(false);
        }

        /// <summary>
        /// Get history from file name.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="fullpath">Full path.</param>
        /// <returns>Srobble history.</returns>
        private async Task<(SimklHistory History, BaseItemDto Item)> GetHistoryFromFileName(BaseItemDto item, bool fullpath = true)
        {
            var fname = fullpath ? item.Path : Path.GetFileName(item.Path);
            var mo = await GetFromFile(fname).ConfigureAwait(false);
            if (mo == null)
            {
                throw new InvalidDataException("Search file response is null");
            }

            var history = new SimklHistory();
            if (mo.Movie != null &&
                (item.IsMovie == true || item.Type == BaseItemKind.Movie))
            {
                if (!string.Equals(mo.Type, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != movie (" + mo.Type + ")");
                }

                item.Name = mo.Movie.Title;
                item.ProductionYear = mo.Movie.Year;
                history.Movies.Add(mo.Movie);
            }
            else if (mo.Episode != null
                     && mo.Show != null
                     && (item.IsSeries == true || item.Type == BaseItemKind.Episode))
            {
                if (!string.Equals(mo.Type, "episode", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != episode (" + mo.Type + ")");
                }

                item.Name = mo.Episode.Title;
                item.SeriesName = mo.Show.Title;
                item.IndexNumber = mo.Episode.Episode;
                item.ParentIndexNumber = mo.Episode.Season;
                item.ProductionYear = mo.Show.Year;
                history.Episodes.Add(mo.Episode);
            }

            return (history, item);
        }

        private static HttpRequestMessage GetOptions(string? userToken = null)
        {
            var requestMessage = new HttpRequestMessage();
            requestMessage.Headers.TryAddWithoutValidation("simkl-api-key", Apikey);
            if (!string.IsNullOrEmpty(userToken))
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            }

            return requestMessage;
        }

        private static SimklHistory CreateHistoryFromItem(BaseItemDto item)
        {
            var history = new SimklHistory();

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                history.Movies.Add(new SimklMovie(item));
            }
            else if (item.IsSeries == true || (item.Type == BaseItemKind.Series))
            {
                // Jellyfin sends episode id instead of show id
                // TODO: TV Shows scrobbling (WIP)
                history.Shows.Add(new SimklShow(item));
            }
            else if (item.Type == BaseItemKind.Episode)
            {
                history.Episodes.Add(new SimklEpisode(item));
            }

            return history;
        }

        [Obsolete("Used only by the deprecated SyncPlaybackAsync method.")]
        private static SimklPlayback CreatePlaybackFromItem(BaseItemDto item, float percentageWatched)
        {
            var playback = new SimklPlayback();
            var progress = (int)percentageWatched;

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                playback.Movies.Add(new SimklMoviePlayback
                {
                    Title = item.OriginalTitle,
                    Year = item.ProductionYear,
                    Ids = new SimklMovieIds(item.ProviderIds ?? new Dictionary<string, string>()),
                    Progress = progress
                });
            }
            else if (item.IsSeries == true || item.Type == BaseItemKind.Series)
            {
                playback.Shows.Add(new SimklShowPlayback
                {
                    Title = item.Name,
                    Year = item.ProductionYear,
                    Ids = new SimklShowIds(item.ProviderIds ?? new Dictionary<string, string>()),
                    Progress = progress
                });
            }
            else if (item.Type == BaseItemKind.Episode)
            {
                playback.Episodes.Add(new SimklEpisodePlayback
                {
                    Title = item.Name,
                    Season = item.ParentIndexNumber,
                    Episode = item.IndexNumber,
                    Ids = new SimklIds(item.ProviderIds ?? new Dictionary<string, string>()),
                    Progress = progress
                });
            }

            return playback;
        }

        /// <summary>
        /// Builds the body for a /scrobble/* call.
        /// </summary>
        /// <param name="item">The item being watched. For episodes this is the episode, carrying
        /// the episode's own provider ids and the *series* production year.</param>
        /// <param name="percentageWatched">Progress as a percentage (0-100).</param>
        /// <param name="seriesProviderIds">Provider ids of the parent series, for episodes.</param>
        /// <returns>The scrobble request body.</returns>
        private static ScrobbleRequest CreateScrobbleRequestFromItem(
            BaseItemDto item,
            float percentageWatched,
            IReadOnlyDictionary<string, string>? seriesProviderIds)
        {
            var request = new ScrobbleRequest { Progress = percentageWatched };
            var itemProviderIds = item.ProviderIds ?? new Dictionary<string, string>();

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                request.Movie = new ScrobbleMovie
                {
                    // OriginalTitle is empty for a lot of libraries, and sending a null title
                    // leaves Simkl nothing to match on when there are no usable ids either.
                    Title = string.IsNullOrEmpty(item.OriginalTitle) ? item.Name : item.OriginalTitle,
                    Year = item.ProductionYear,
                    Ids = NullIfEmpty(new SimklMovieIds(itemProviderIds))
                };
            }
            else if (item.Type == BaseItemKind.Episode)
            {
                // Simkl identifies the show from show.ids (falling back to title+year) and the
                // episode from season+number. The series ids therefore belong on the show, not
                // on the episode - sending them as episode ids makes the lookup fail.
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
            }

            return request;
        }

        /// <summary>
        /// Drops an ids object that resolved no ids at all, so it is omitted from the request and
        /// Simkl falls back to title+year matching.
        /// </summary>
        /// <typeparam name="T">The ids type.</typeparam>
        /// <param name="ids">The ids object.</param>
        /// <returns>The ids object, or <c>null</c> when it holds no ids.</returns>
        private static T? NullIfEmpty<T>(T ids)
            where T : SimklIds
        {
            return ids.HasAnyId() ? ids : null;
        }

        /// <summary>
        /// Reads the Retry-After header, which may be either delta-seconds or an HTTP date.
        /// </summary>
        /// <param name="response">The response to inspect.</param>
        /// <returns>How long to wait, or <c>null</c> when the server did not say.</returns>
        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null)
            {
                return null;
            }

            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }

            return null;
        }

        private static string Truncate(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= MaxLoggedBodyLength
                ? value
                : value.Substring(0, MaxLoggedBodyLength) + "... (truncated)";
        }

        /// <summary>
        /// Deprecated. Use <see cref="ScrobbleStartAsync"/> instead.
        /// Posts to the legacy /sync/playback endpoint.
        /// </summary>
        /// <param name="item">Item currently being watched.</param>
        /// <param name="userToken">User token.</param>
        /// <param name="percentageWatched">Percentage of item watched.</param>
        /// <returns>The sync playback response.</returns>
        [Obsolete("Use ScrobbleStartAsync instead, which posts to /scrobble/start.")]
        public async Task<SyncPlaybackResponse?> SyncPlaybackAsync(BaseItemDto item, string userToken, float percentageWatched)
        {
            _logger.LogDebug("Syncing playback for {ItemName} at {Percentage:F1}%", item.Name, percentageWatched);
#pragma warning disable CS0618
            var playback = CreatePlaybackFromItem(item, percentageWatched);
#pragma warning restore CS0618
            return await Post<SyncPlaybackResponse, SimklPlayback>("/sync/playback", userToken, playback).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls /scrobble/start to report that playback has begun or resumed.
        /// </summary>
        /// <param name="item">Item currently being watched.</param>
        /// <param name="userToken">User token.</param>
        /// <param name="percentageWatched">Current playback progress as a percentage (0-100).</param>
        /// <param name="seriesProviderIds">Provider ids of the parent series, when scrobbling an episode.</param>
        /// <returns>The scrobble response.</returns>
        public Task<SyncPlaybackResponse?> ScrobbleStartAsync(
            BaseItemDto item,
            string userToken,
            float percentageWatched,
            IReadOnlyDictionary<string, string>? seriesProviderIds = null)
        {
            return ScrobbleAsync("/scrobble/start", item, userToken, percentageWatched, seriesProviderIds);
        }

        /// <summary>
        /// Calls /scrobble/pause to save progress when playback is paused.
        /// </summary>
        /// <param name="item">The item being paused.</param>
        /// <param name="userToken">User token.</param>
        /// <param name="percentageWatched">Percentage watched when paused.</param>
        /// <param name="seriesProviderIds">Provider ids of the parent series, when scrobbling an episode.</param>
        /// <returns>The scrobble response.</returns>
        public Task<SyncPlaybackResponse?> ScrobblePauseAsync(
            BaseItemDto item,
            string userToken,
            float percentageWatched,
            IReadOnlyDictionary<string, string>? seriesProviderIds = null)
        {
            return ScrobbleAsync("/scrobble/pause", item, userToken, percentageWatched, seriesProviderIds);
        }

        /// <summary>
        /// Calls /scrobble/stop to report that playback has ended.
        /// Simkl marks the item as watched automatically when progress is >= 80%.
        /// </summary>
        /// <param name="item">Item that was watched.</param>
        /// <param name="userToken">User token.</param>
        /// <param name="percentageWatched">Final playback progress as a percentage (0-100).</param>
        /// <param name="seriesProviderIds">Provider ids of the parent series, when scrobbling an episode.</param>
        /// <returns>The scrobble response.</returns>
        public Task<SyncPlaybackResponse?> ScrobbleStopAsync(
            BaseItemDto item,
            string userToken,
            float percentageWatched,
            IReadOnlyDictionary<string, string>? seriesProviderIds = null)
        {
            return ScrobbleAsync("/scrobble/stop", item, userToken, percentageWatched, seriesProviderIds);
        }

        private async Task<SyncPlaybackResponse?> ScrobbleAsync(
            string endpoint,
            BaseItemDto item,
            string userToken,
            float percentageWatched,
            IReadOnlyDictionary<string, string>? seriesProviderIds)
        {
            var request = CreateScrobbleRequestFromItem(item, percentageWatched, seriesProviderIds);

            _logger.LogDebug(
                "POST {Endpoint} for {ItemName} at {Percentage:F1}%: {RequestData}",
                endpoint,
                item.Name,
                percentageWatched,
                JsonSerializer.Serialize(request, _jsonSerializerOptions));

            var response = await Post<SyncPlaybackResponse, ScrobbleRequest>(endpoint, userToken, request).ConfigureAwait(false);

            // Simkl also reports failures as an "error" field on an HTTP 200.
            if (response != null && !string.IsNullOrEmpty(response.Error))
            {
                SimklErrorHandler.LogError(_logger, response.Error, item, request);
            }

            return response;
        }

        /// <summary>
        /// Implements /sync/history method from simkl.
        /// </summary>
        /// <param name="history">History object.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>The sync history response.</returns>
        private async Task<SyncHistoryResponse?> SyncHistoryAsync(SimklHistory history, string userToken)
        {
            _logger.LogInformation("Syncing History");
            return await Post<SyncHistoryResponse, SimklHistory>("/sync/history", userToken, history).ConfigureAwait(false);
        }

        /// <summary>
        /// API's private get method, given RELATIVE url and headers.
        /// </summary>
        /// <param name="url">Relative url.</param>
        /// <param name="userToken">Authentication token.</param>
        /// <returns>The deserialized response.</returns>
        private async Task<T?> Get<T>(string url, string? userToken = null)
        {
            using var options = GetOptions(userToken);
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Get;
            return await SendAsync<T>(options, url, userToken).ConfigureAwait(false);
        }

        /// <summary>
        /// API's private post method.
        /// </summary>
        /// <param name="url">Relative post url.</param>
        /// <param name="userToken">Authentication token.</param>
        /// <param name="data">Object to serialize.</param>
        private async Task<T1?> Post<T1, T2>(string url, string? userToken = null, T2? data = null)
            where T2 : class
        {
            using var options = GetOptions(userToken);
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Post;

            if (data != null)
            {
                options.Content = new StringContent(
                    JsonSerializer.Serialize(data, _jsonSerializerOptions),
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json);
            }

            return await SendAsync<T1>(options, url, userToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a request and deserializes the response, turning HTTP-level failures into
        /// exceptions.
        /// </summary>
        /// <remarks>
        /// HttpClient.SendAsync does NOT throw for 4xx/5xx, so the status has to be inspected by
        /// hand. Without this, a 401 from an expired token was fed straight into the JSON
        /// deserializer: the token was never cleared, the user was still shown as signed in, and
        /// the only trace was a JsonException logged at Trace level.
        /// </remarks>
        private async Task<T?> SendAsync<T>(HttpRequestMessage options, string relativeUrl, string? userToken)
        {
            using var responseMessage = await _httpClientFactory
                .CreateClient(NamedClient.Default)
                .SendAsync(options)
                .ConfigureAwait(false);

            var body = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (responseMessage.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (!string.IsNullOrEmpty(userToken))
                {
                    _logger.LogError(
                        "Simkl rejected the user token for {Url} (401 Unauthorized). Clearing the stored token; the user has to sign in to Simkl again from the plugin settings.",
                        relativeUrl);
                    SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                }
                else
                {
                    _logger.LogError("Simkl returned 401 Unauthorized for {Url} and no user token was sent.", relativeUrl);
                }

                throw new InvalidTokenException($"Simkl returned 401 Unauthorized for {relativeUrl}");
            }

            if (!responseMessage.IsSuccessStatusCode)
            {
                var truncated = Truncate(body);
                var retryAfter = GetRetryAfter(responseMessage);

                _logger.LogWarning(
                    "Simkl returned {StatusCode} {ReasonPhrase} for {Url} (retry after: {RetryAfter}): {Body}",
                    (int)responseMessage.StatusCode,
                    responseMessage.ReasonPhrase,
                    relativeUrl,
                    retryAfter,
                    truncated);

                throw new SimklApiException(
                    $"Simkl returned HTTP {(int)responseMessage.StatusCode} for {relativeUrl}",
                    responseMessage.StatusCode,
                    truncated,
                    retryAfter);
            }

            _logger.LogDebug("Simkl API raw response for {Url}: {Body}", relativeUrl, Truncate(body));

            if (string.IsNullOrWhiteSpace(body))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, _caseInsensitiveJsonSerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new SimklApiException(
                    $"Could not deserialize the Simkl response for {relativeUrl}",
                    null,
                    Truncate(body),
                    ex);
            }
        }
    }
}
