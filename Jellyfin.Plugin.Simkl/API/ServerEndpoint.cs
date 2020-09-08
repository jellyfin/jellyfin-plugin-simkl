using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using Microsoft.Extensions.Logging;
#pragma warning disable CA1801

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Server endpoints.
    /// </summary>
    public class ServerEndpoint : IService, IHasResultFactory
    {
        private readonly SimklApi _api;
        private readonly ILogger<ServerEndpoint> _logger;
        private readonly IJsonSerializer _json;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerEndpoint"/> class.
        /// </summary>
        /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
        /// <param name="json">Instance of the <see cref="IJsonSerializer"/> interface.</param>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        public ServerEndpoint(ILoggerFactory loggerFactory, IJsonSerializer json, IHttpClient httpClient)
        {
            _logger = loggerFactory.CreateLogger<ServerEndpoint>();
            _json = json;
            _api = new SimklApi(json, loggerFactory.CreateLogger<SimklApi>(), httpClient);
        }

        /// <summary>
        /// Gets or sets result factory.
        /// </summary>
        public IHttpResultFactory ResultFactory { get; set; }

        /// <summary>
        /// Gets or sets request.
        /// </summary>
        public IRequest Request { get; set; }

        /// <summary>
        /// Get pin request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The code response.</returns>
        public CodeResponse Get(GetPin request)
        {
            return _api.GetCode().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get pin status.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Code status response.</returns>
        public CodeStatusResponse Get(GetPinStatus request)
        {
            return _api.GetCodeStatus(request.user_code).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get user settings.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>User settings.</returns>
        public UserSettings Get(GetUserSettings request)
        {
            _logger.LogDebug(_json.SerializeToString(request));
            return _api.GetUserSettings(SimklPlugin.Instance.Configuration.GetByGuid(request.UserId).UserToken).GetAwaiter().GetResult();
        }
    }
}