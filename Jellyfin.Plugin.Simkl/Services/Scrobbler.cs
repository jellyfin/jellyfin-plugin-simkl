using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <inheritdoc />
    public class Scrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager; // Needed to set up de startPlayBack and endPlayBack functions
        private readonly ILogger<Scrobbler> _logger;
        private readonly IJsonSerializer _json;
        private readonly INotificationManager _notifications;
        private readonly Dictionary<string, Guid> _lastScrobbled; // Library ID of last scrobbled item
        private SimklApi _api;
        private DateTime _nextTry;

        /// <summary>
        /// Initializes a new instance of the <see cref="Scrobbler"/> class.
        /// </summary>
        /// <param name="json">Instance of the <see cref="IJsonSerializer"/> interface.</param>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="loggerFactory">Instance of the <see cref="ILogger{Scrobbler}"/> interface.</param>
        /// <param name="httpClient">Instance of the <see cref="IHttpClient"/> interface.</param>
        /// <param name="notifications">Instance of the <see cref="INotificationManager"/> interface.</param>
        public Scrobbler(
            IJsonSerializer json,
            ISessionManager sessionManager,
            ILoggerFactory loggerFactory,
            IHttpClient httpClient,
            INotificationManager notifications)
        {
            _json = json;
            _sessionManager = sessionManager;
            _logger = loggerFactory.CreateLogger<Scrobbler>();
            _notifications = notifications;
            _api = new SimklApi(json, loggerFactory.CreateLogger<SimklApi>(), httpClient);
            _lastScrobbled = new Dictionary<string, Guid>();
            _nextTry = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public Task RunAsync()
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
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
                _api = null;
            }
        }

        /// <summary>
        /// Session can be scrobbled.
        /// </summary>
        /// <param name="config">The plugin configuration.</param>
        /// <param name="session">The session.</param>
        /// <returns>If session can be scrobbled.</returns>
        private static bool CanBeScrobbled(UserConfig config, SessionInfo session)
        {
            if (session.NowPlayingItem.RunTimeTicks != null)
            {
                var percentageWatched = session.PlayState.PositionTicks / (float)session.NowPlayingItem.RunTimeTicks * 100f;

                // If percentage watched is below minimum, can't scrobble
                if (percentageWatched < config.ScrobblePercentage)
                {
                    return false;
                }
            }

            // If it's below minimum length, can't scrobble
            if (session.NowPlayingItem.RunTimeTicks < 60 * 10000 * config.MinLength)
            {
                return false;
            }

            var item = session.FullNowPlayingItem;
            return item switch
            {
                Movie _ => config.ScrobbleMovies,
                Episode _ => config.ScrobbleShows,
                _ => false
            };
        }

        private bool CanSendNotification(BaseItemDto item)
        {
            if (item.IsMovie == true || item.Type == "Movie")
            {
                return _notifications.GetNotificationTypes().Any(t => t.Type == SimklNotificationsFactory.NotificationMovieType && t.Enabled);
            }

            if (item.IsSeries == true || item.Type == "Episode")
            {
                return _notifications.GetNotificationTypes().Any(t => t.Type == SimklNotificationsFactory.NotificationShowType && t.Enabled);
            }

            return false;
        }

        private async void OnPlaybackProgress(object sessions, PlaybackProgressEventArgs e)
        {
            var sid = e.PlaySessionId;
            Guid uid = e.Session.UserId, npid = e.Session.NowPlayingItem.Id;
            try
            {
                if (DateTime.UtcNow < _nextTry)
                {
                    return;
                }

                _nextTry = DateTime.UtcNow.AddSeconds(30);

                var userConfig = SimklPlugin.Instance.Configuration.GetByGuid(uid);
                if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
                {
                    _logger.LogError("Can't scrobble: User " + e.Session.UserName + " not logged in (" + (userConfig == null) + ")");
                    return;
                }

                if (!CanBeScrobbled(userConfig, e.Session))
                {
                    return;
                }

                if (_lastScrobbled.ContainsKey(sid) && _lastScrobbled[sid] == npid)
                {
                    _logger.LogDebug("Already scrobbled {0} for {1}", e.Session.NowPlayingItem.Name, e.Session.UserName);
                    return;
                }

                _logger.LogDebug(_json.SerializeToString(e.Session.NowPlayingItem));
                _logger.LogInformation(
                    "Trying to scrobble {0} ({1}) for {2} ({3}) - {4} on {5}",
                    e.Session.NowPlayingItem.Name,
                    npid,
                    e.Session.UserName,
                    uid,
                    e.Session.NowPlayingItem.Path,
                    sid);

                var response = await _api.MarkAsWatched(e.MediaInfo, userConfig.UserToken).ConfigureAwait(false);
                if (response.success)
                {
                    _logger.LogDebug("Scrobbled without errors");
                    _lastScrobbled[sid] = npid;

                    if (CanSendNotification(response.item))
                    {
                        await _notifications.SendNotification(
                                SimklNotificationsFactory.GetNotificationRequest(response.item, e.Session.UserId),
                                e.Session.FullNowPlayingItem,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (InvalidTokenException)
            {
                _logger.LogDebug("Deleted user token");
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Couldn't scrobble.");
                _lastScrobbled[sid] = npid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Caught unknown exception while trying to scrobble.");
            }
        }
    }
}