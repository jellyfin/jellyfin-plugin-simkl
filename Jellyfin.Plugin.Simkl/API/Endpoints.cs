using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Controller.Library; // CORRECTED: This namespace is needed for both ILibraryManager and IUserManager
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="Endpoints"/> class.
        /// </summary>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        public Endpoints(SimklApi simklApi, ILibraryManager libraryManager, IUserManager userManager)
        {
            _simklApi = simklApi;
            _libraryManager = libraryManager;
            _userManager = userManager;
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
        /// Starts a full library sync for a user.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <returns>A status result.</returns>
        [HttpPost("sync/full/{userId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult StartFullSync([FromRoute] Guid userId)
        {
            var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
            {
                return Unauthorized("User not configured for Simkl.");
            }

            // We start the sync as a background task
            // so the web request is not blocked.
            Task.Run(() => _simklApi.SyncLibrary(userConfig, _libraryManager, _userManager));

            return NoContent(); // 204 is a good response for a "fire and forget" action
        }
    }
}