using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using Jellyfin.Plugin.Simkl.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// The simkl endpoints.
    /// </summary>
    /// <remarks>
    /// The class-level <see cref="AuthorizeAttribute"/> also covers the login routes, which were anonymous in the
    /// Emby original. That is a deliberate tightening: every one of them names the user being signed in, and
    /// <see cref="AuthorizedTarget"/> then decides whether the caller may act on that user.
    /// </remarks>
    [ApiController]
    [Authorize]
    [Route("Simkl")]
    public class Endpoints : ControllerBase
    {
        // how many polls in a row may fail to reach Simkl before the login is given up on; at the five second
        // floor this rides out about a minute of trouble without burning the fifteen minute code
        private const int TransientPollFailureLimit = 12;

        private readonly SimklApi _simklApi;
        private readonly HistoryImporter _importer;
        private readonly DeviceLoginStore _logins;
        private readonly IUserManager _userManager;
        private readonly IAuthorizationContext _authContext;
        private readonly ILogger<Endpoints> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Endpoints"/> class.
        /// </summary>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="importer">Instance of the <see cref="HistoryImporter"/>.</param>
        /// <param name="logins">Instance of the <see cref="DeviceLoginStore"/>.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="authContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{Endpoints}"/> interface.</param>
        public Endpoints(
            SimklApi simklApi,
            HistoryImporter importer,
            DeviceLoginStore logins,
            IUserManager userManager,
            IAuthorizationContext authContext,
            ILogger<Endpoints> logger)
        {
            _simklApi = simklApi;
            _importer = importer;
            _logins = logins;
            _userManager = userManager;
            _authContext = authContext;
            _logger = logger;
        }

        /// <summary>
        /// Starts an OAuth 2 device login for one user.
        /// </summary>
        /// <remarks>
        /// The device code stays on the server; the page only ever sees the code the user types and where to type
        /// it. Starting a second login for the same user replaces the first.
        /// </remarks>
        /// <param name="userId">The Jellyfin user being signed in to Simkl.</param>
        /// <response code="200">The login was started, or could not be and says so in status.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The login status.</returns>
        [HttpPost("oauth/device/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceLoginStatus>> StartDeviceLogin([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            if (target == null)
            {
                return error ?? NotFound();
            }

            var code = await _simklApi.StartDeviceLogin(HttpContext.RequestAborted).ConfigureAwait(false);
            if (code == null || string.IsNullOrEmpty(code.DeviceCode) || string.IsNullOrEmpty(code.UserCode))
            {
                return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusError, Message = "Simkl wouldn't issue a code" };
            }

            var seconds = code.ExpiresIn ?? 900;
            var interval = Math.Max(5, code.Interval ?? 5);
            _logins.Start(userId, code.DeviceCode, DateTime.UtcNow.AddSeconds(seconds), interval);
            _logger.LogInformation("Started a Simkl device login for {UserName}", target.Username);

            return new DeviceLoginStatus
            {
                Status = DeviceLoginStatus.StatusPending,
                UserCode = code.UserCode,
                VerificationUri = code.VerificationUri,
                VerificationUriComplete = code.VerificationUriComplete,
                ExpiresIn = seconds,
                Interval = interval
            };
        }

        /// <summary>
        /// Asks whether a user's device login has been approved yet, and stores the tokens once it has.
        /// </summary>
        /// <param name="userId">The Jellyfin user being signed in to Simkl.</param>
        /// <response code="200">Whether the login is still pending, done, or over.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The login status.</returns>
        [HttpPost("oauth/device/{userId}/poll")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceLoginStatus>> PollDeviceLogin([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            if (target == null)
            {
                return error ?? NotFound();
            }

            var pending = _logins.Get(userId);
            if (pending == null)
            {
                return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusExpired };
            }

            TokenResponse? tokens;
            string? reason;
            try
            {
                (tokens, reason) = await _simklApi.PollDeviceLogin(pending.DeviceCode, HttpContext.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                // a timeout, a reset connection, or an HTML error page from whatever fronts api.simkl.com. Same
                // class as a 502 body: it says nothing about the login, so it joins the transient path below
                // rather than escaping as a 500 that would make the page abort.
                _logger.LogDebug(ex, "The Simkl device poll for {UserName} did not complete", target.Username);
                tokens = null;
                reason = ex.GetType().Name;
            }

            if (tokens?.AccessToken != null)
            {
                // A cancel, a logout or a second sign in may have landed while this poll was on the wire, which is
                // seconds long. Dropping exactly the attempt this poll started with both says whether it is still
                // the current one and leaves a newer attempt alone; without it a login the user abandoned comes
                // back, holding a token that was just minted.
                if (!_logins.Cancel(userId, pending))
                {
                    _logger.LogInformation(
                        "A Simkl device login of {UserName} was approved after it had been replaced or cancelled; ignoring it",
                        target.Username);
                    return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusExpired };
                }

                SimklPlugin.Instance?.Configuration.StoreLogin(
                    userId,
                    tokens.AccessToken,
                    tokens.RefreshToken ?? string.Empty,
                    DateTime.UtcNow.AddSeconds(tokens.ExpiresIn ?? SimklApi.DefaultAccessLifetimeSeconds),
                    tokens.Scope ?? string.Empty);
                _logger.LogInformation("Connected {UserName} to Simkl", target.Username);
                return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusConnected };
            }

            // slow_down is the five second floor being hit; the page simply waits out the next interval
            var slowDown = string.Equals(reason, OAuthErrorResponse.SlowDown, StringComparison.Ordinal);
            if (slowDown || string.Equals(reason, OAuthErrorResponse.AuthorizationPending, StringComparison.Ordinal))
            {
                pending.ClearFailures();
                return new DeviceLoginStatus
                {
                    Status = DeviceLoginStatus.StatusPending,
                    ExpiresIn = pending.SecondsLeft(),
                    Interval = slowDown ? pending.Interval + 5 : pending.Interval
                };
            }

            // Anything that is not an answer about this login - a 502 from whatever fronts api.simkl.com, a rate
            // limit, a dropped connection - says nothing about the device code, which is still good and may even
            // have been approved already. Keep the code and let the page poll through it, but not forever.
            if (!OAuthErrorResponse.IsTerminal(reason))
            {
                var failures = pending.RecordFailure();
                if (failures < TransientPollFailureLimit)
                {
                    _logger.LogWarning(
                        "Simkl did not answer the device login of {UserName} ({Error}); attempt {Failures} of {Limit}, keeping the code",
                        target.Username,
                        reason,
                        failures,
                        TransientPollFailureLimit);
                    return new DeviceLoginStatus
                    {
                        Status = DeviceLoginStatus.StatusPending,
                        ExpiresIn = pending.SecondsLeft(),
                        Interval = pending.Interval
                    };
                }

                _logins.Cancel(userId, pending);
                _logger.LogError(
                    "Gave up on the device login of {UserName} after {Failures} answers Simkl could not be reached for; last was {Error}",
                    target.Username,
                    failures,
                    reason);
                return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusError, Message = "Simkl could not be reached. Please try again." };
            }

            _logins.Cancel(userId, pending);
            if (string.Equals(reason, OAuthErrorResponse.ExpiredToken, StringComparison.Ordinal))
            {
                return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusExpired };
            }

            _logger.LogError("Simkl refused the device login of {UserName}: {Error}", target.Username, reason);
            return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusError, Message = reason };
        }

        /// <summary>
        /// Drops a device login a user did not finish.
        /// </summary>
        /// <param name="userId">The Jellyfin user whose login is dropped.</param>
        /// <response code="200">The login was dropped, or there was none.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The login status.</returns>
        [HttpPost("oauth/device/{userId}/cancel")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<DeviceLoginStatus>> CancelDeviceLogin([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            if (target == null)
            {
                return error ?? NotFound();
            }

            _logins.Cancel(userId);
            return new DeviceLoginStatus { Status = DeviceLoginStatus.StatusExpired };
        }

        /// <summary>
        /// Starts a full import of the Simkl history of a user.
        /// </summary>
        /// <param name="userId">The Jellyfin user whose Simkl history is imported.</param>
        /// <response code="200">The import status, with the reason in error when it was refused.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The import status.</returns>
        [HttpPost("import/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImportStatus>> StartImport([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            return target == null ? error ?? NotFound() : _importer.TryStart(target, Config(userId));
        }

        /// <summary>
        /// Forgets what was imported before and imports the whole Simkl history again, refreshing the watch dates
        /// of items Jellyfin already has as played.
        /// </summary>
        /// <param name="userId">The Jellyfin user whose import memory is cleared.</param>
        /// <response code="200">The import status, with the reason in error when it was refused.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The import status.</returns>
        [HttpPost("import/{userId}/reset")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImportStatus>> StartResetImport([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            return target == null ? error ?? NotFound() : _importer.TryStart(target, Config(userId), true);
        }

        /// <summary>
        /// Runs the check the scheduled task would run: everything on the first run, only what changed afterwards.
        /// </summary>
        /// <param name="userId">The Jellyfin user whose Simkl history is checked.</param>
        /// <param name="automatic">True when switching automatic import on: the check then counts as the first automatic run.</param>
        /// <response code="200">The import status, with the reason in error when it was refused.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The import status.</returns>
        [HttpPost("import/{userId}/check")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImportStatus>> StartAutoCheck([FromRoute] Guid userId, [FromQuery] bool automatic)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            return target == null ? error ?? NotFound() : _importer.TryStartAuto(target, Config(userId), automatic);
        }

        /// <summary>
        /// Reads how the last, or the running, import of a user is doing.
        /// </summary>
        /// <param name="userId">The Jellyfin user whose import status is read.</param>
        /// <response code="200">The status was read.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The import status.</returns>
        [HttpGet("import/{userId}/status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImportStatus>> GetImportStatus([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            return target == null ? error ?? NotFound() : _importer.Status(target);
        }

        /// <summary>
        /// Reads the Simkl user id and name behind the stored token, so the page can link to the user's own Simkl pages.
        /// </summary>
        /// <param name="userId">The Jellyfin user whose Simkl account is read.</param>
        /// <response code="200">The account, empty when the user is not logged in to Simkl.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The Simkl account.</returns>
        [HttpGet("account/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SimklAccount>> GetAccount([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            if (target == null)
            {
                return error ?? NotFound();
            }

            var config = Config(userId);
            if (string.IsNullOrEmpty(config.UserToken))
            {
                return new SimklAccount();
            }

            try
            {
                var settings = await _simklApi.GetUserSettings(config).ConfigureAwait(false);
                return new SimklAccount
                {
                    Id = settings?.Account?.Id,
                    Name = settings?.User?.Name,
                    Plan = settings?.Account?.Type,

                    // read after the call, not before: a renewal during it may have replaced the token
                    Legacy = !config.IsOAuth2
                };
            }
            catch (InvalidTokenException)
            {
                // the token was cleared; an empty account is what tells the page to render the login again
                return new SimklAccount();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Couldn't read the Simkl account of {UserName}", target.Username);
                return new SimklAccount();
            }
        }

        /// <summary>
        /// Disconnects a user from Simkl by clearing their stored token.
        /// </summary>
        /// <remarks>
        /// This is a server action rather than a configuration save on purpose. The page saves by posting the whole
        /// plugin configuration, and <see cref="PluginConfiguration.PreserveServerState"/> deliberately refuses an
        /// empty token in a posted snapshot (it cannot tell a deliberate logout from a snapshot taken before a login),
        /// so a logout done that way would be written straight back.
        /// </remarks>
        /// <param name="userId">The Jellyfin user to disconnect.</param>
        /// <response code="200">The user was disconnected.</response>
        /// <response code="403">The caller may not act on this user.</response>
        /// <response code="404">The user does not exist.</response>
        /// <returns>The Simkl account, now empty.</returns>
        [HttpPost("logout/{userId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<SimklAccount>> LogOut([FromRoute] Guid userId)
        {
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            if (target == null)
            {
                return error ?? NotFound();
            }

            _logins.Cancel(userId);
            SimklPlugin.Instance?.Configuration.LogOut(userId);
            _logger.LogInformation("Disconnected {UserName} from Simkl", target.Username);

            // a real body, not 204: the page reads JSON back from every route here
            return new SimklAccount();
        }

        // The caller may act on itself and an administrator on anyone. An api key may act on any user too, which is
        // deliberate and wider than the Emby original: Jellyfin api keys are issued from the dashboard and are
        // admin-equivalent by convention, and a key is how a script would drive an import. Drop the IsApiKey arm to
        // require a real user session instead.
        private async Task<(JellyfinUser? Target, ActionResult? Error)> AuthorizedTarget(Guid userId)
        {
            var authInfo = await _authContext.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
            var caller = authInfo.User;
            if (caller == null && !authInfo.IsApiKey)
            {
                return (null, Unauthorized());
            }

            var target = _userManager.GetUserById(userId);
            if (target == null)
            {
                return (null, NotFound());
            }

            if (caller != null && caller.Id != target.Id && !caller.HasPermission(PermissionKind.IsAdministrator))
            {
                return (null, Forbid());
            }

            return (target, null);
        }

        private static UserConfig Config(Guid userId)
        {
            return SimklPlugin.Instance?.Configuration.GetByGuid(userId) ?? new UserConfig { Id = userId };
        }

        /// <summary>
        /// Sends a scrobble start (now watching) update for the given item.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="item">The item being watched.</param>
        /// <param name="progress">Playback progress as a percentage (0–100).</param>
        /// <returns>The scrobble response.</returns>
        [HttpPost("scrobble/start/{userId}")]
        public async Task<ActionResult<ScrobbleResponse?>> ScrobbleStart(
            [FromRoute] Guid userId,
            [FromBody] MediaBrowser.Model.Dto.BaseItemDto item,
            [FromQuery] float progress)
        {
            // before the settings are read, not after: whether a user has connected a Simkl account is itself
            // something only they and an administrator may learn
            var (target, error) = await AuthorizedTarget(userId).ConfigureAwait(false);
            if (target == null)
            {
                return error ?? NotFound();
            }

            var userConfiguration = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfiguration == null || string.IsNullOrEmpty(userConfiguration.UserToken))
            {
                return Unauthorized();
            }

            // no series ids to offer on this route: the caller posts one item, so an episode is identified by the
            // show title and its season and number
            var request = SimklApi.BuildRequest(item, progress, null);
            if (request == null)
            {
                return BadRequest("Only a movie or an episode can be scrobbled.");
            }

            var (status, response, _) = await _simklApi.ScrobbleStart(request, userConfiguration, CancellationToken.None).ConfigureAwait(false);
            if ((int)status >= 400)
            {
                // Simkl's own answer, not a bodyless success: this route exists for callers outside the plugin
                return StatusCode((int)status);
            }

            return response;
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
        public Task<ActionResult<ScrobbleResponse?>> SyncPlayback(
            [FromRoute] Guid userId,
            [FromBody] MediaBrowser.Model.Dto.BaseItemDto item)
        {
            // the route is kept, but Simkl retired /sync/playback in favour of /scrobble/*, so it now reports a start
            // at the beginning of the item, which is what the old call did
            return ScrobbleStart(userId, item, 0);
        }
    }
}
