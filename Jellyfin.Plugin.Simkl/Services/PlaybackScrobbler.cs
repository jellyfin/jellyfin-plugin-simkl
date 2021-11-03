using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Playback progress scrobbler.
    /// </summary>
    public class PlaybackScrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager; // Needed to set up de startPlayBack and endPlayBack functions
        private readonly ILogger<PlaybackScrobbler> _logger;
        private readonly Dictionary<string, Guid> _lastScrobbled; // Library ID of last scrobbled item
        private readonly SimklApi _simklApi;
        private DateTime _nextTry;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackScrobbler"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{Scrobbler}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        public PlaybackScrobbler(
            ISessionManager sessionManager,
            ILogger<PlaybackScrobbler> logger,
            SimklApi simklApi)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _simklApi = simklApi;
            _lastScrobbled = new Dictionary<string, Guid>();
            _nextTry = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public Task RunAsync()
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Dispose all resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sessionManager.PlaybackProgress -= OnPlaybackProgress;
                _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            }
        }

        private static bool CanBeScrobbled(UserConfig config, PlaybackProgressEventArgs playbackProgress)
        {
            var position = playbackProgress.PlaybackPositionTicks;
            var runtime = playbackProgress.MediaInfo.RunTimeTicks;

            if (runtime != null)
            {
                var percentageWatched = position / (float)runtime * 100f;

                // If percentage watched is below minimum, can't scrobble
                if (percentageWatched < config.ScrobblePercentage)
                {
                    return false;
                }
            }

            // If it's below minimum length, can't scrobble
            if (runtime < 60 * 10000 * config.MinLength)
            {
                return false;
            }

            return playbackProgress.MediaInfo.Type switch
            {
                BaseItemKind.Movie => config.ScrobbleMovies,
                BaseItemKind.Episode => config.ScrobbleShows,
                _ => false
            };
        }

        private async void OnPlaybackProgress(object? sessions, PlaybackProgressEventArgs e)
        {
            if (DateTime.UtcNow < _nextTry)
            {
                return;
            }

            _nextTry = DateTime.UtcNow.AddSeconds(30);
            await ScrobbleSession(e);
        }

        private async void OnPlaybackStopped(object? sessions, PlaybackStopEventArgs e)
        {
            await ScrobbleSession(e);
        }

        private async Task ScrobbleSession(PlaybackProgressEventArgs eventArgs)
        {
            try
            {
                var userId = eventArgs.Session.UserId;
                var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
                if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
                {
                    _logger.LogError(
                        "Can't scrobble: User {UserName} not logged in ({UserConfigStatus})",
                        eventArgs.Session.UserName,
                        userConfig == null);
                    return;
                }

                if (!CanBeScrobbled(userConfig, eventArgs))
                {
                    return;
                }

                if (_lastScrobbled.ContainsKey(eventArgs.Session.Id) && _lastScrobbled[eventArgs.Session.Id] == eventArgs.MediaInfo.Id)
                {
                    _logger.LogDebug("Already scrobbled {ItemName} for {UserName}", eventArgs.MediaInfo.Name, eventArgs.Session.UserName);
                    return;
                }

                _logger.LogInformation(
                    "Trying to scrobble {Name} ({NowPlayingId}) for {UserName} ({UserId}) - {PlayingItemPath} on {SessionId}",
                    eventArgs.MediaInfo.Name,
                    eventArgs.MediaInfo.Id,
                    eventArgs.Session.UserName,
                    userId,
                    eventArgs.MediaInfo.Path,
                    eventArgs.Session.Id);

                var response = await _simklApi.MarkAsWatched(eventArgs.MediaInfo, userConfig.UserToken);
                if (response.Success)
                {
                    _logger.LogDebug("Scrobbled without errors");
                    _lastScrobbled[eventArgs.Session.Id] = eventArgs.MediaInfo.Id;
                }
            }
            catch (InvalidTokenException)
            {
                _logger.LogDebug("Deleted user token");
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Couldn't scrobble");
                _lastScrobbled[eventArgs.Session.Id] = eventArgs.MediaInfo.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Caught unknown exception while trying to scrobble");
            }
        }
    }
}
