using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging; // using System.Threading;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Simkl Api.
    /// </summary>
    public class SimklApi
    {
        /* INTERFACES */
        private readonly IJsonSerializer _json;
        private readonly ILogger<SimklApi> _logger;
        private readonly IHttpClient _httpClient;

        /* BASIC API THINGS */

        /// <summary>
        /// Base url.
        /// </summary>
        public const string Baseurl = @"https://api.simkl.com";
        // public const string BASE_URL = @"http://private-9c39b-simkl.apiary-proxy.com";

        /// <summary>
        /// Redirect uri.
        /// </summary>
        public const string RedirectUri = @"https://simkl.com/apps/emby/connected/";

        /// <summary>
        /// Api key.
        /// </summary>
        public const string Apikey = @"27dd5d6adc24aa1ad9f95ef913244cbaf6df5696036af577ed41670473dc97d0";

        /// <summary>
        /// Secret.
        /// </summary>
        public const string Secret = @"d7b9feb9d48bbaa69dbabaca21ba4671acaa89198637e9e136a4d69ec97ab68b";

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApi"/> class.
        /// </summary>
        /// <param name="json">Instance of the <see cref="IJsonSerializer"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{SimklApi}"/> interface.</param>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        public SimklApi(IJsonSerializer json, ILogger<SimklApi> logger, IHttpClient httpClient)
        {
            _json = json;
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Get code.
        /// </summary>
        /// <returns>Code response.</returns>
        public async Task<CodeResponse> GetCode()
        {
            var uri = $"/oauth/pin?client_id={Apikey}&redirect={RedirectUri}";
            return _json.DeserializeFromStream<CodeResponse>(await Get(uri).ConfigureAwait(false));
        }

        /// <summary>
        /// Get code status.
        /// </summary>
        /// <param name="userCode">User code.</param>
        /// <returns>Code status.</returns>
        public async Task<CodeStatusResponse> GetCodeStatus(string userCode)
        {
            var uri = $"/oauth/pin/{userCode}?client_id={Apikey}";
            return _json.DeserializeFromStream<CodeStatusResponse>(await Get(uri).ConfigureAwait(false));
        }

        /// <summary>
        /// Get user settings.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns>User settings.</returns>
        public async Task<UserSettings> GetUserSettings(string userToken)
        {
            try
            {
                return _json.DeserializeFromStream<UserSettings>(await Post("/users/settings/", userToken).ConfigureAwait(false));
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
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
        public async Task<(bool success, BaseItemDto item)> MarkAsWatched(BaseItemDto item, string userToken)
        {
            var history = CreateHistoryFromItem(item);
            var r = await SyncHistoryAsync(history, userToken).ConfigureAwait(false);
            _logger.LogDebug("Response: " + _json.SerializeToString(r));
            if (history.Movies.Count == r.Added.Movies && history.Shows.Count == r.Added.Shows)
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
            _logger.LogDebug("Response: " + _json.SerializeToString(r));

            return (history.Movies.Count == r.Added.Movies && history.Shows.Count == r.Added.Shows, item);
        }

        /// <summary>
        /// Get from file.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <returns>Search file response.</returns>
        private async Task<SearchFileResponse> GetFromFile(string filename)
        {
            var f = new SimklFile { File = filename };
            _logger.LogInformation("Posting: " + _json.SerializeToString(f));
            using var r = new StreamReader(await Post("/search/file/", null, f).ConfigureAwait(false));
            var t = r.ReadToEnd();
            _logger.LogDebug("Response: " + t);
            return _json.DeserializeFromString<SearchFileResponse>(t);
        }

        /// <summary>
        /// Get history from file name.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="fullpath">Full path.</param>
        /// <returns>Srobble history.</returns>
        private async Task<(SimklHistory history, BaseItemDto item)> GetHistoryFromFileName(BaseItemDto item, bool fullpath = true)
        {
            var fname = fullpath ? item.Path : Path.GetFileName(item.Path);
            var mo = await GetFromFile(fname).ConfigureAwait(false);

            var history = new SimklHistory();
            if (item.IsMovie == true || item.Type == "Movie")
            {
                if (mo.Type != "movie")
                {
                    throw new InvalidDataException("type != movie (" + mo.Type + ")");
                }

                item.Name = mo.Movie.Title;
                item.ProductionYear = mo.Movie.Year;
                history.Movies.Add(mo.Movie);
            }
            else if (item.IsSeries == true || item.Type == "Episode")
            {
                if (mo.Type != "episode")
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

        private static HttpRequestOptions GetOptions(string userToken = null)
        {
            var options = new HttpRequestOptions
            {
                RequestContentType = "application/json",
                LogErrorResponseBody = true,
                EnableDefaultUserAgent = true
            };
            options.RequestHeaders.Add("simkl-api-key", Apikey);
            // options.RequestHeaders.Add("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(userToken))
            {
                options.RequestHeaders.Add("Authorization", "Bearer " + userToken);
            }

            return options;
        }

        private static SimklHistory CreateHistoryFromItem(BaseItemDto item)
        {
            var history = new SimklHistory();

            if (item.IsMovie == true || item.Type == "Movie")
            {
                history.Movies.Add(new SimklMovie(item));
            }
            else if (item.IsSeries == true || item.Type == "Episode")
            {
                // TODO: TV Shows scrobbling (WIP)
                history.Shows.Add(new SimklShow(item));
            }

            return history;
        }

        /// <summary>
        /// Implements /sync/history method from simkl.
        /// </summary>
        /// <param name="history">History object.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>The sync history response.</returns>
        private async Task<SyncHistoryResponse> SyncHistoryAsync(SimklHistory history, string userToken)
        {
            try
            {
                _logger.LogInformation("Syncing History: " + _json.SerializeToString(history));
                return _json.DeserializeFromStream<SyncHistoryResponse>(await Post("/sync/history", userToken, history).ConfigureAwait(false));
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Invalid user token " + userToken + ", deleting");
                SimklPlugin.Instance.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token " + userToken);
            }
        }

        /// <summary>
        /// API's private get method, given RELATIVE url and headers.
        /// </summary>
        /// <param name="url">Relative url.</param>
        /// <param name="userToken">Authentication token.</param>
        /// <returns>HTTP(s) Stream to be used.</returns>
        private async Task<Stream> Get(string url, string userToken = null)
        {
            // Todo: If string is not null neither empty
            var options = GetOptions(userToken);
            options.Url = Baseurl + url;

            return await _httpClient.Get(options).ConfigureAwait(false);
        }

        /// <summary>
        /// API's private post method.
        /// </summary>
        /// <param name="url">Relative post url.</param>
        /// <param name="userToken">Authentication token.</param>
        /// <param name="data">Object to serialize.</param>
        private async Task<Stream> Post(string url, string userToken = null, object data = null)
        {
            var options = GetOptions(userToken);
            options.Url = Baseurl + url;
            if (data != null)
            {
                options.RequestContent = _json.SerializeToString(data);
            }

            return (await _httpClient.Post(options).ConfigureAwait(false)).Content;
        }
    }
}