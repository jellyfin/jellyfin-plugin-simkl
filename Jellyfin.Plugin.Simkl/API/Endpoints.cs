using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// The simkl endpoints.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("Simkl")]
    public class Endpoints : ControllerBase
    {
        private readonly SimklApi _simklApi;

        /// <summary>
        /// Initializes a new instance of the <see cref="Endpoints"/> class.
        /// </summary>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        public Endpoints(SimklApi simklApi)
        {
            _simklApi = simklApi;
        }

        /// <summary>
        /// Gets the oauth pin.
        /// </summary>
        /// <returns>The oauth pin.</returns>
        [HttpGet("oauth/pin")]
        public async Task<ActionResult<CodeResponse?>> GetPin()
        {
            return await _simklApi.GetCode();
        }

        /// <summary>
        /// Gets the status for the code.
        /// </summary>
        /// <param name="userCode">The user auth code.</param>
        /// <returns>The code status response.</returns>
        [HttpGet("oauth/pin/{userCode}")]
        public async Task<ActionResult<CodeStatusResponse?>> GetPinStatus([FromRoute] string userCode)
        {
            return await _simklApi.GetCodeStatus(userCode);
        }

        /// <summary>
        /// Gets the settings for the user.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>The user settings.</returns>
        [HttpGet("users/settings/{userId}")]
        public async Task<ActionResult<UserSettings?>> GetUserSettings([FromRoute] Guid userId)
        {
            var userConfiguration = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfiguration == null)
            {
                return NotFound();
            }

            return await _simklApi.GetUserSettings(userConfiguration.UserToken);
        }

        /// <summary>
        /// Sends a scrobble start (now watching) update for the given item.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="item">The item being watched.</param>
        /// <param name="progress">Playback progress as a percentage (0–100).</param>
        /// <returns>The scrobble response.</returns>
        [HttpPost("scrobble/start/{userId}")]
        public async Task<ActionResult<SyncPlaybackResponse?>> ScrobbleStart(
            [FromRoute] Guid userId,
            [FromBody] MediaBrowser.Model.Dto.BaseItemDto item,
            [FromQuery] float progress)
        {
            var userConfiguration = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfiguration == null || string.IsNullOrEmpty(userConfiguration.UserToken))
            {
                return Unauthorized();
            }

            return await _simklApi.ScrobbleStartAsync(item, userConfiguration.UserToken, progress);
        }

        /// <summary>
        /// Deprecated. Use <see cref="ScrobbleStart"/> instead.
        /// Posts to the legacy Simkl /sync/playback endpoint.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="item">The item being watched.</param>
        /// <returns>The sync playback response.</returns>
        [HttpPost("sync/playback/{userId}")]
        [Obsolete("Use POST /Simkl/scrobble/start/{userId}?progress=<float> instead.")]
        public async Task<ActionResult<SyncPlaybackResponse?>> SyncPlayback(
            [FromRoute] Guid userId,
            [FromBody] MediaBrowser.Model.Dto.BaseItemDto item)
        {
            var userConfiguration = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfiguration == null || string.IsNullOrEmpty(userConfiguration.UserToken))
            {
                return Unauthorized();
            }

#pragma warning disable CS0618
            return await _simklApi.SyncPlaybackAsync(item, userConfiguration.UserToken, 0);
#pragma warning restore CS0618
        }
    }
}