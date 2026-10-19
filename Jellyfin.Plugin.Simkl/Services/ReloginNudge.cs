using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Reminds users still signed in through the old pin flow to sign in again.
    /// </summary>
    /// <remarks>
    /// Those logins keep working and are never broken on purpose, so nothing forces the issue: the reminder is a
    /// toast on the client, at most once every ten days per user, until the user reconnects and the token stops
    /// being a pin flow one. It is raised when a playback starts because that is the one moment the user is
    /// provably in front of a session that can show it.
    /// </remarks>
    public class ReloginNudge
    {
        private const string Header = "Simkl";

        // One short line, because jellyfin-web gives it three seconds and no title. Its DisplayMessage handler
        // tests TimeoutMs for presence only and then calls toast(), which is hardcoded to 3000ms and takes no
        // duration; it also passes only the text on, so the Header above never reaches the screen there and the
        // message has to name Simkl itself. Other clients may honour the value, so a generous one is still sent.
        private const string Body = "Simkl: reconnect your account in the plugin settings - the old sign-in is being retired.";

        // Present, not a duration: its presence is what picks a toast over a modal dialog on the web client.
        private const long ToastMs = 12000;

        private static readonly TimeSpan Interval = TimeSpan.FromDays(10);

        private readonly ISessionManager _sessionManager;
        private readonly ILogger<ReloginNudge> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReloginNudge"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{ReloginNudge}"/> interface.</param>
        public ReloginNudge(ISessionManager sessionManager, ILogger<ReloginNudge> logger)
        {
            _sessionManager = sessionManager;
            _logger = logger;
        }

        /// <summary>
        /// Shows the reminder on one session, when this user is due one.
        /// </summary>
        /// <param name="config">The settings of the user watching.</param>
        /// <param name="sessionId">The session to show it on.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes once the reminder was sent, or skipped.</returns>
        public async Task Remind(UserConfig config, string sessionId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(config.UserToken) || config.IsOAuth2)
            {
                return;
            }

            // a cheap look before the configuration lock; the lock repeats it, since that is where it is decided
            if (config.LastReloginPromptAt != default && DateTime.UtcNow - config.LastReloginPromptAt < Interval)
            {
                return;
            }

            // the stamp is the claim: of two sessions starting at once exactly one is told to show the toast
            if (SimklPlugin.Instance?.Configuration.MarkReloginPrompted(config.Id, config.UserToken, Interval) != true)
            {
                return;
            }

            try
            {
                await _sessionManager.SendMessageCommand(
                    sessionId,
                    sessionId,
                    new MessageCommand { Header = Header, Text = Body, TimeoutMs = ToastMs },
                    cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Asked session {Session} to reconnect its Simkl login; not again for {Days} days", sessionId, Interval.TotalDays);
            }
            catch (OperationCanceledException)
            {
                // the server is going down; the next playback raises it again
            }
            catch (Exception ex)
            {
                // a client that cannot show a message must not cost the playback its scrobble
                _logger.LogDebug(ex, "Couldn't show the Simkl reconnect reminder on session {Session}", sessionId);
            }
        }
    }
}
