using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Simkl Api.
    /// </summary>
    public class SimklApi
    {
        private readonly ILogger<SimklApi> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly JsonSerializerOptions _caseInsensitiveJsonSerializerOptions;

        /// <summary>
        /// Base url for the Simkl API.
        /// </summary>
        public const string Baseurl = @"https://api.simkl.com";

        /// <summary>
        /// Redirect uri for OAuth.
        /// </summary>
        public const string RedirectUri = @"https://simkl.com/apps/jellyfin/connected/";

        /// <summary>
        /// Api key for the Simkl application.
        /// </summary>
        public const string Apikey = @"c721b22482097722a84a20ccc579cf9d232be85b9befe7b7805484d0ddbc6781";

        /// <summary>
        /// Secret for the Simkl application.
        /// </summary>
        public const string Secret = @"87893fc73cdbd2e51a7c63975c6f941ac1c6155c0e20ffa76b83202dd10a507e";

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
        /// Get code for device authentication.
        /// </summary>
        /// <returns>A <see cref="Task"/> containing the code response.</returns>
        public async Task<CodeResponse?> GetCode()
        {
            var uri = $"/oauth/pin?client_id={Apikey}&redirect={RedirectUri}";
            return await Get<CodeResponse>(uri);
        }

        /// <summary>
        /// Get status of a device authentication code.
        /// </summary>
        /// <param name="userCode">User code.</param>
        /// <returns>A <see cref="Task"/> containing the code status.</returns>
        public async Task<CodeStatusResponse?> GetCodeStatus(string userCode)
        {
            var uri = $"/oauth/pin/{userCode}?client_id={Apikey}";
            return await Get<CodeStatusResponse>(uri);
        }

        /// <summary>
        /// Get user settings from Simkl.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns>A <see cref="Task"/> containing the user settings.</returns>
        public async Task<UserSettings?> GetUserSettings(string userToken)
        {
            try
            {
                return await Post<UserSettings, object>("/users/settings/", userToken);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new UserSettings { Error = "user_token_failed" };
            }
        }

        /// <summary>
        /// Mark an item as watched on Simkl.
        /// </summary>
        /// <param name="item">The item to mark as watched.</param>
        /// <param name="userToken">The user's authentication token.</param>
        /// <returns>A <see cref="Task"/> containing a tuple indicating success and the item.</returns>
        public async Task<(bool Success, BaseItemDto Item)> MarkAsWatched(BaseItemDto item, string userToken)
        {
            var history = CreateHistoryFromItem(item);
            var r = await SyncHistoryAsync(history, userToken);
            _logger.LogDebug("BaseItem: {@Item}", item);
            _logger.LogDebug("History: {@History}", history);
            _logger.LogDebug("Response: {@Response}", r);
            if (r != null && history.Movies.Count == r.Added.Movies
                && history.Shows.Count == r.Added.Shows
                && history.Episodes.Count == r.Added.Episodes)
            {
                return (true, item);
            }

            try
            {
                (history, item) = await GetHistoryFromFileName(item);
            }
            catch (InvalidDataException)
            {
                _logger.LogDebug("Couldn't scrobble using full path, trying using only filename");
                (history, item) = await GetHistoryFromFileName(item, false);
            }

            r = await SyncHistoryAsync(history, userToken);
            return r == null
                ? (false, item)
                : (history.Movies.Count == r.Added.Movies && history.Shows.Count == r.Added.Shows, item);
        }

        /// <summary>
        /// Syncs the full library for a user.
        /// </summary>
        /// <param name="config">The user's plugin configuration.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="userManager">The user manager.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task SyncLibrary(UserConfig config, ILibraryManager libraryManager, IUserManager userManager)
        {
            _logger.LogInformation("Starting full library sync for user {UserId}", config.Id);

            var user = userManager.GetUserById(config.Id);
            if (user == null)
            {
                _logger.LogError("Could not find user for full sync: {UserId}", config.Id);
                return;
            }

            var history = new SimklHistory();

            var movies = libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsPlayed = true,
                Recursive = true
            });

            foreach (var movie in movies)
            {
                history.Movies.Add(new SimklMovie(movie));
            }

            _logger.LogInformation("Found {MovieCount} watched movies to sync for user {UserId}", history.Movies.Count, config.Id);

            var episodes = libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true
            });

            foreach (var episode in episodes)
            {
                history.Episodes.Add(new SimklEpisode(episode));
            }

            _logger.LogInformation("Found {EpisodeCount} watched episodes to sync for user {UserId}", history.Episodes.Count, config.Id);

            if (history.Movies.Count > 0 || history.Episodes.Count > 0)
            {
                try
                {
                    var response = await SyncHistoryAsync(history, config.UserToken);
                    if (response != null)
                    {
                        _logger.LogInformation("Full sync response: {AddedMovies} movies, {AddedEpisodes} episodes added.", response.Added.Movies, response.Added.Episodes);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during full library sync for user {UserId}", config.Id);
                }
            }
            else
            {
                _logger.LogInformation("No watched items found to sync for user {UserId}", config.Id);
            }
        }

        private async Task<SearchFileResponse?> GetFromFile(string filename)
        {
            var f = new SimklFile { File = filename };
            _logger.LogInformation("Posting: {@File}", f);
            return await Post<SearchFileResponse, SimklFile>("/search/file/", null, f);
        }

        private async Task<(SimklHistory history, BaseItemDto item)> GetHistoryFromFileName(BaseItemDto item, bool fullpath = true)
        {
            var fname = fullpath ? item.Path : Path.GetFileName(item.Path);
            var mo = await GetFromFile(fname);
            if (mo == null)
            {
                throw new InvalidDataException("Search file response is null");
            }

            var history = new SimklHistory();
            if (mo.Movie != null && item.Type == BaseItemKind.Movie)
            {
                if (!string.Equals(mo.Type, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != movie (" + mo.Type + ")");
                }

                item.Name = mo.Movie.Title;
                item.ProductionYear = mo.Movie.Year;
                history.Movies.Add(mo.Movie);
            }
            else if (mo.Episode != null && mo.Show != null && item.Type == BaseItemKind.Episode)
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

            if (item.Type == BaseItemKind.Movie)
            {
                history.Movies.Add(new SimklMovie(item));
            }
            else if (item.Type == BaseItemKind.Series)
            {
                history.Shows.Add(new SimklShow(item));
            }
            else if (item.Type == BaseItemKind.Episode)
            {
                history.Episodes.Add(new SimklEpisode(item));
            }

            return history;
        }

        private async Task<SyncHistoryResponse?> SyncHistoryAsync(SimklHistory history, string userToken)
        {
            try
            {
                _logger.LogInformation("Syncing History");
                return await Post<SyncHistoryResponse, SimklHistory>("/sync/history", userToken, history);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError(e, "Invalid user token {UserToken}, deleting", userToken);
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token " + userToken);
            }
        }

        private async Task<T?> Get<T>(string url, string? userToken = null)
        {
            using var options = GetOptions(userToken);
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Get;
            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options);
            return await responseMessage.Content.ReadFromJsonAsync<T>(_jsonSerializerOptions);
        }

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

            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options);

            return await responseMessage.Content.ReadFromJsonAsync<T1>(_caseInsensitiveJsonSerializerOptions);
        }
    }
}