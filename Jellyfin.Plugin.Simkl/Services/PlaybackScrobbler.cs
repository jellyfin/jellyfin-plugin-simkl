using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Playback progress scrobbler.
    /// </summary>
    /// <remarks>
    /// The session state only ever advances after Simkl has actually accepted a call. Anything
    /// that was not sent - throttled, failed, skipped - leaves the state untouched so the next
    /// playback event retries it. Advancing state on a call that never happened is what used to
    /// wedge a session into "already started" and silently stop all scrobbling until Jellyfin
    /// was restarted.
    /// </remarks>
    public class PlaybackScrobbler : IHostedService
    {
        /// <summary>
        /// Number of consecutive failures after which the current item is given up on, so a
        /// permanently unmatchable item does not retry on every progress event forever.
        /// </summary>
        private const int MaxConsecutiveFailures = 3;

        /// <summary>
        /// Attempts made for the terminal /scrobble/stop call, which cannot be retried later by a
        /// subsequent playback event because there will not be one.
        /// </summary>
        private const int StopAttempts = 3;

        /// <summary>
        /// Only start evicting idle sessions once more than this many are tracked.
        /// </summary>
        private const int PruneThreshold = 64;

        /// <summary>
        /// Progress at which Simkl marks an item as watched on /scrobble/stop. Used only to
        /// explain in the log why a stop below it did not mark anything as watched.
        /// </summary>
        private const float SimklWatchedThreshold = 80f;

        /// <summary>
        /// Minimum time between two scrobble calls for the same session. Absorbs event storms
        /// (pause/resume spam, duplicate progress reports) from a single client.
        /// </summary>
        private static readonly TimeSpan MinSessionCallInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Minimum time between two scrobble calls for the same user, across all of their
        /// sessions. This is the actual ceiling on how hard one account can hit Simkl; the
        /// per-session interval alone would multiply by the number of devices in use.
        /// </summary>
        /// <remarks>
        /// Simkl documents no numeric rate limit (only that HTTP 429 exists), so this is a
        /// self-imposed courtesy limit. Because a call that hits it is deferred and retried on the
        /// next playback event rather than dropped, the value can be conservative without costing
        /// correctness - normal playback only needs a handful of calls per item anyway.
        /// </remarks>
        private static readonly TimeSpan MinUserCallInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long to hold off all calls for a user after an HTTP 429 that carried no
        /// Retry-After header.
        /// </summary>
        private static readonly TimeSpan DefaultRateLimitCooldown = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Upper bound on a Retry-After we will honour, so an implausible value cannot park a user
        /// indefinitely.
        /// </summary>
        private static readonly TimeSpan MaxRateLimitCooldown = TimeSpan.FromMinutes(15);

        /// <summary>
        /// How long the terminal stop call is willing to wait out an active cooldown before
        /// giving up. Waiting is preferable to dropping it: there is no later event to retry on.
        /// </summary>
        private static readonly TimeSpan MaxStopWait = TimeSpan.FromSeconds(30);

        /// <summary>
        /// How long to wait before retrying after a failed scrobble call.
        /// </summary>
        private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How often an already-started scrobble is re-sent while playback continues, so Simkl's
        /// "now watching" entry does not expire.
        /// </summary>
        private static readonly TimeSpan ProgressRefreshInterval = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Delay between attempts at the terminal stop call.
        /// </summary>
        private static readonly TimeSpan StopRetryDelay = TimeSpan.FromSeconds(2);

        /// <summary>
        /// A session that has not reported progress for this long is treated as a finished
        /// playback. This is what recovers sessions whose client died without ever sending
        /// PlaybackStopped.
        /// </summary>
        private static readonly TimeSpan SessionStaleAfter = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long an idle session is kept before being evicted.
        /// </summary>
        private static readonly TimeSpan SessionRetention = TimeSpan.FromHours(6);

        private readonly ISessionManager _sessionManager;
        private readonly ILogger<PlaybackScrobbler> _logger;
        private readonly SimklApi _simklApi;
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Per-session scrobble state, keyed by Jellyfin session id.
        /// </summary>
        private readonly ConcurrentDictionary<string, SessionScrobbleTracker> _sessions;

        /// <summary>
        /// Per-user call ceiling, and the holding pen for server-imposed 429 cooldowns.
        /// </summary>
        /// <remarks>
        /// Hitting this defers a call; it must never advance the session state, or the state
        /// machine would believe a call it never made had succeeded.
        /// </remarks>
        private readonly RateLimiter<Guid> _userRateLimiter;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackScrobbler"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PlaybackScrobbler}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        public PlaybackScrobbler(
            ISessionManager sessionManager,
            ILogger<PlaybackScrobbler> logger,
            SimklApi simklApi,
            ILibraryManager libraryManager)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _simklApi = simklApi;
            _libraryManager = libraryManager;
            _sessions = new ConcurrentDictionary<string, SessionScrobbleTracker>(StringComparer.Ordinal);
            _userRateLimiter = new RateLimiter<Guid>(MinUserCallInterval);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _sessions.Clear();
            return Task.CompletedTask;
        }

        private static bool CanStartScrobbling(UserConfig config, PlaybackProgressEventArgs playbackProgress)
        {
            var runtime = playbackProgress.MediaInfo?.RunTimeTicks;

            // Must have a known runtime above the configured minimum length
            if (!runtime.HasValue || runtime.Value < 60L * 10000 * config.MinLength)
            {
                return false;
            }

            return playbackProgress.MediaInfo!.Type switch
            {
                BaseItemKind.Movie => config.ScrobbleMovies,
                BaseItemKind.Episode => config.ScrobbleShows,
                _ => false
            };
        }

        private static bool TryGetPercentageWatched(PlaybackProgressEventArgs playbackProgress, out float percentageWatched)
        {
            percentageWatched = 0f;

            var runtime = playbackProgress.MediaInfo?.RunTimeTicks;
            var position = playbackProgress.PlaybackPositionTicks;

            if (!runtime.HasValue || runtime.Value <= 0 || !position.HasValue)
            {
                return false;
            }

            percentageWatched = Math.Clamp((float)position.Value / runtime.Value * 100f, 0f, 100f);
            return true;
        }

        private static PlaybackProgressEventArgs CreateProgressArgsFromStop(PlaybackStopEventArgs stopArgs)
        {
            return new PlaybackProgressEventArgs
            {
                MediaInfo = stopArgs.MediaInfo,
                Session = stopArgs.Session,
                PlaybackPositionTicks = stopArgs.PlaybackPositionTicks
            };
        }

        // Sync wrappers - async void is avoided so that faults stay observable.
        private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            Observe(HandleProgressAsync(e), "playback progress");
        }

        private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            Observe(HandleStoppedAsync(e), "playback stopped");
        }

        private void Observe(Task task, string handlerName)
        {
            _ = task.ContinueWith(
                t => _logger.LogError(t.Exception, "Unhandled exception in the Simkl {HandlerName} handler", handlerName),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private SessionScrobbleTracker? GetTracker(SessionInfo? session, BaseItemDto? mediaInfo, DateTime nowUtc)
        {
            if (session == null || mediaInfo == null || string.IsNullOrEmpty(session.Id))
            {
                return null;
            }

            return _sessions.GetOrAdd(session.Id, _ => new SessionScrobbleTracker(mediaInfo.Id, nowUtc));
        }

        private async Task HandleProgressAsync(PlaybackProgressEventArgs e)
        {
            var now = DateTime.UtcNow;
            var tracker = GetTracker(e.Session, e.MediaInfo, now);
            if (tracker == null)
            {
                return;
            }

            // A progress event while a call is already in flight for this session is not worth
            // queueing: another one follows shortly.
            if (!await tracker.Gate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await HandleStateChangeAsync(tracker, e, now).ConfigureAwait(false);
            }
            finally
            {
                tracker.Gate.Release();
            }
        }

        private async Task HandleStoppedAsync(PlaybackStopEventArgs e)
        {
            var now = DateTime.UtcNow;
            var tracker = GetTracker(e.Session, e.MediaInfo, now);
            if (tracker == null)
            {
                return;
            }

            await tracker.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await HandleStopAsync(tracker, e, now).ConfigureAwait(false);
            }
            finally
            {
                tracker.Gate.Release();
            }

            PruneIdleSessions(DateTime.UtcNow);
        }

        /// <summary>
        /// Rebinds the tracker when the incoming event clearly belongs to a different playback
        /// than the one it currently describes.
        /// </summary>
        private void SyncTrackerToPlayback(SessionScrobbleTracker tracker, PlaybackProgressEventArgs e, DateTime nowUtc)
        {
            var itemId = e.MediaInfo!.Id;
            var itemChanged = tracker.ItemId != itemId;
            var wentStale = nowUtc - tracker.LastEventUtc > SessionStaleAfter;

            if (itemChanged || wentStale)
            {
                if (tracker.State is SessionScrobbleState.Started or SessionScrobbleState.Paused)
                {
                    _logger.LogDebug(
                        "Resetting Simkl scrobble state for session {SessionId} ({Reason}); the previous playback never reported a stop",
                        e.Session.Id,
                        itemChanged ? "item changed" : "no progress for " + SessionStaleAfter);
                }

                tracker.ResetFor(itemId);
            }

            tracker.LastEventUtc = nowUtc;
        }

        private async Task HandleStateChangeAsync(SessionScrobbleTracker tracker, PlaybackProgressEventArgs e, DateTime nowUtc)
        {
            SyncTrackerToPlayback(tracker, e, nowUtc);

            var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(e.Session.UserId);
            if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
            {
                return;
            }

            if (!CanStartScrobbling(userConfig, e))
            {
                return;
            }

            if (tracker.Abandoned)
            {
                return;
            }

            // Playback already finished for this item. Trailing progress events must not open a
            // new scrobble, because no further stop event will arrive to close it.
            if (tracker.State == SessionScrobbleState.Stopped)
            {
                return;
            }

            var isPaused = e.Session.PlayState?.IsPaused ?? false;
            var desiredState = isPaused ? SessionScrobbleState.Paused : SessionScrobbleState.Started;

            if (tracker.State == desiredState)
            {
                // Simkl expires a "now watching" entry that stops being refreshed, so an active
                // scrobble is re-affirmed periodically. This doubles as the backstop that recovers
                // a session still marked as playing an item whose playback ended without a stop
                // event.
                var dueForRefresh = desiredState == SessionScrobbleState.Started
                                    && nowUtc - tracker.LastSuccessUtc >= ProgressRefreshInterval;

                if (!dueForRefresh)
                {
                    return;
                }
            }

            if (nowUtc < tracker.NextAttemptUtc)
            {
                _logger.LogDebug(
                    "Deferring scrobble {DesiredState} for {Name} until {NextAttempt:HH:mm:ss}Z; will retry on the next playback event",
                    desiredState,
                    e.MediaInfo!.Name,
                    tracker.NextAttemptUtc);
                return;
            }

            var userId = e.Session.UserId;
            if (!_userRateLimiter.CanExecute(userId, nowUtc))
            {
                _logger.LogDebug(
                    "Deferring scrobble {DesiredState} for {Name} until {NextAllowed:HH:mm:ss}Z to stay within the Simkl call rate for {UserName}; will retry on the next playback event",
                    desiredState,
                    e.MediaInfo!.Name,
                    _userRateLimiter.GetNextAllowed(userId),
                    e.Session.UserName);
                return;
            }

            // Reserve the slot before the call, not after: an exception mid-call must still
            // consume it, otherwise a failing Simkl gets hammered on every progress event.
            _userRateLimiter.MarkExecuted(userId, nowUtc);

            var action = isPaused ? ScrobbleAction.Pause : ScrobbleAction.Start;
            var outcome = await SendScrobbleAsync(action, e, userConfig).ConfigureAwait(false);

            RecordAttempt(tracker, outcome, desiredState, e);
        }

        private async Task HandleStopAsync(SessionScrobbleTracker tracker, PlaybackStopEventArgs e, DateTime nowUtc)
        {
            var progressArgs = CreateProgressArgsFromStop(e);
            SyncTrackerToPlayback(tracker, progressArgs, nowUtc);

            var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(e.Session.UserId);
            if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
            {
                return;
            }

            if (tracker.State == SessionScrobbleState.Stopped)
            {
                return;
            }

            var hadOpenScrobble = tracker.State is SessionScrobbleState.Started or SessionScrobbleState.Paused;

            if (!CanStartScrobbling(userConfig, progressArgs))
            {
                tracker.State = SessionScrobbleState.Stopped;
                return;
            }

            // Send the stop whenever Simkl has an open scrobble for this item, even below the
            // configured percentage: /scrobble/stop only marks the item watched past Simkl's own
            // threshold, and without it the "now watching" entry lingers indefinitely.
            var reachedConfiguredPercentage = TryGetPercentageWatched(progressArgs, out var percentageWatched)
                                              && percentageWatched >= userConfig.ScrobblePercentage;

            if (!hadOpenScrobble && !reachedConfiguredPercentage)
            {
                tracker.State = SessionScrobbleState.Stopped;
                return;
            }

            if (reachedConfiguredPercentage && percentageWatched < SimklWatchedThreshold)
            {
                _logger.LogInformation(
                    "{Name} stopped at {Progress:F1}%, which meets the configured {Configured}% but is below the {Threshold}% Simkl requires to mark an item watched",
                    e.MediaInfo!.Name,
                    percentageWatched,
                    userConfig.ScrobblePercentage,
                    SimklWatchedThreshold);
            }

            // Not throttled and retried in place: this is the call that actually records the
            // watch, and there is no later playback event to retry it on.
            await SendStopWithRetryAsync(progressArgs, userConfig).ConfigureAwait(false);

            tracker.State = SessionScrobbleState.Stopped;
        }

        private void RecordAttempt(
            SessionScrobbleTracker tracker,
            ScrobbleOutcome outcome,
            SessionScrobbleState desiredState,
            PlaybackProgressEventArgs e)
        {
            var now = DateTime.UtcNow;

            switch (outcome)
            {
                case ScrobbleOutcome.Succeeded:
                    tracker.State = desiredState;
                    tracker.ConsecutiveFailures = 0;
                    tracker.LastSuccessUtc = now;
                    tracker.NextAttemptUtc = now + MinSessionCallInterval;
                    return;

                case ScrobbleOutcome.Skipped:
                    // Nothing was sent and nothing failed; leave the state alone so the next
                    // event tries again.
                    return;

                case ScrobbleOutcome.Permanent:
                    tracker.Abandoned = true;
                    _logger.LogWarning(
                        "Giving up on scrobbling {Name} for {UserName}: Simkl rejected the request in a way retrying will not fix. Scrobbling resumes on the next item.",
                        e.MediaInfo!.Name,
                        e.Session.UserName);
                    return;

                default:
                    tracker.ConsecutiveFailures++;
                    tracker.NextAttemptUtc = now + FailureBackoff;

                    if (tracker.ConsecutiveFailures >= MaxConsecutiveFailures)
                    {
                        tracker.Abandoned = true;
                        _logger.LogWarning(
                            "Giving up on scrobbling {Name} for {UserName} after {Attempts} failed attempts. Scrobbling resumes on the next item.",
                            e.MediaInfo!.Name,
                            e.Session.UserName,
                            tracker.ConsecutiveFailures);
                    }

                    return;
            }
        }

        private async Task<bool> SendStopWithRetryAsync(PlaybackProgressEventArgs e, UserConfig userConfig)
        {
            var userId = e.Session.UserId;

            for (var attempt = 1; attempt <= StopAttempts; attempt++)
            {
                // The stop call waits out the rate limit rather than skipping it. Dropping it is
                // what silently lost completed watches; blocking a detached handler for a few
                // seconds after playback has ended costs nothing.
                if (!await WaitOutRateLimitAsync(userId, e).ConfigureAwait(false))
                {
                    return false;
                }

                _userRateLimiter.MarkExecuted(userId, DateTime.UtcNow);

                var outcome = await SendScrobbleAsync(ScrobbleAction.Stop, e, userConfig).ConfigureAwait(false);

                if (outcome is ScrobbleOutcome.Succeeded or ScrobbleOutcome.Skipped)
                {
                    return outcome == ScrobbleOutcome.Succeeded;
                }

                if (outcome == ScrobbleOutcome.Permanent || attempt == StopAttempts)
                {
                    _logger.LogWarning(
                        "Could not record the completed playback of {Name} for {UserName} with Simkl after {Attempts} attempt(s); this watch is lost",
                        e.MediaInfo!.Name,
                        e.Session.UserName,
                        attempt);
                    return false;
                }

                await Task.Delay(StopRetryDelay * attempt).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// Waits for an active rate-limit cooldown to expire, up to <see cref="MaxStopWait"/>.
        /// </summary>
        /// <returns><c>true</c> when the caller may proceed; <c>false</c> when the wait is too long.</returns>
        private async Task<bool> WaitOutRateLimitAsync(Guid userId, PlaybackProgressEventArgs e)
        {
            var nextAllowed = _userRateLimiter.GetNextAllowed(userId);
            if (nextAllowed == null)
            {
                return true;
            }

            var remaining = nextAllowed.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return true;
            }

            if (remaining > MaxStopWait)
            {
                _logger.LogWarning(
                    "Simkl is rate-limiting {UserName} for another {Remaining}; cannot record the completed playback of {Name}",
                    e.Session.UserName,
                    remaining,
                    e.MediaInfo!.Name);
                return false;
            }

            _logger.LogDebug(
                "Waiting {Remaining} for the Simkl rate limit before recording the completed playback of {Name}",
                remaining,
                e.MediaInfo!.Name);
            await Task.Delay(remaining).ConfigureAwait(false);
            return true;
        }

        private async Task<ScrobbleOutcome> SendScrobbleAsync(ScrobbleAction action, PlaybackProgressEventArgs e, UserConfig userConfig)
        {
            if (!TryGetPercentageWatched(e, out var percentageWatched))
            {
                return ScrobbleOutcome.Skipped;
            }

            var itemName = e.MediaInfo!.Name;
            var userName = e.Session.UserName;

            try
            {
                var target = await GetScrobbleTargetAsync(e.MediaInfo).ConfigureAwait(false);

                _logger.LogDebug(
                    "Sending scrobble {Action} for {Name} ({Progress:F1}%) for {UserName}",
                    action,
                    itemName,
                    percentageWatched,
                    userName);

                var response = action switch
                {
                    ScrobbleAction.Start => await _simklApi
                        .ScrobbleStartAsync(target.Item, userConfig.UserToken, percentageWatched, target.SeriesProviderIds)
                        .ConfigureAwait(false),
                    ScrobbleAction.Pause => await _simklApi
                        .ScrobblePauseAsync(target.Item, userConfig.UserToken, percentageWatched, target.SeriesProviderIds)
                        .ConfigureAwait(false),
                    _ => await _simklApi
                        .ScrobbleStopAsync(target.Item, userConfig.UserToken, percentageWatched, target.SeriesProviderIds)
                        .ConfigureAwait(false)
                };

                if (response == null)
                {
                    _logger.LogWarning(
                        "Simkl returned an empty body for scrobble {Action} of {Name}",
                        action,
                        itemName);
                    return ScrobbleOutcome.Transient;
                }

                if (!string.IsNullOrEmpty(response.Error))
                {
                    // SimklApi already logged the specifics via SimklErrorHandler.
                    return SimklErrorHandler.IsTransient(response.Error)
                        ? ScrobbleOutcome.Transient
                        : ScrobbleOutcome.Permanent;
                }

                _logger.LogInformation(
                    "Scrobble {Action} accepted for {Name} by {UserName} at {Progress:F1}% (Simkl action: {ServerAction})",
                    action,
                    itemName,
                    userName,
                    percentageWatched,
                    response.Action);
                return ScrobbleOutcome.Succeeded;
            }
            catch (InvalidTokenException)
            {
                // SimklApi has already cleared the stored token and logged the reason.
                _logger.LogWarning(
                    "Skipping scrobble {Action} of {Name}: {UserName} has to sign in to Simkl again",
                    action,
                    itemName,
                    userName);
                return ScrobbleOutcome.Permanent;
            }
            catch (SimklApiException ex)
            {
                if (ex.IsRateLimited)
                {
                    var cooldown = ex.RetryAfter ?? DefaultRateLimitCooldown;
                    if (cooldown > MaxRateLimitCooldown)
                    {
                        cooldown = MaxRateLimitCooldown;
                    }

                    _userRateLimiter.Defer(e.Session.UserId, DateTime.UtcNow + cooldown);
                    _logger.LogWarning(
                        "Simkl rate-limited scrobble {Action} of {Name}; holding all Simkl calls for {UserName} for {Cooldown}",
                        action,
                        itemName,
                        userName,
                        cooldown);
                    return ScrobbleOutcome.Transient;
                }

                _logger.LogWarning(
                    ex,
                    "Scrobble {Action} of {Name} failed (HTTP {StatusCode}). Response: {Body}",
                    action,
                    itemName,
                    ex.StatusCode,
                    ex.ResponseBody);
                return ex.IsTransient ? ScrobbleOutcome.Transient : ScrobbleOutcome.Permanent;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Network error sending scrobble {Action} for {Name}", action, itemName);
                return ScrobbleOutcome.Transient;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Timed out sending scrobble {Action} for {Name}", action, itemName);
                return ScrobbleOutcome.Transient;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error sending scrobble {Action} for {Name} by {UserName}",
                    action,
                    itemName,
                    userName);
                return ScrobbleOutcome.Transient;
            }
        }

        private async Task<ScrobbleTarget> GetScrobbleTargetAsync(BaseItemDto item)
        {
            if (item.Type != BaseItemKind.Episode)
            {
                return new ScrobbleTarget(item, null);
            }

            try
            {
                var entity = await Task.Run(() => _libraryManager.GetItemById(item.Id)).ConfigureAwait(false);
                if (entity is Episode episode && episode.Series is Series series)
                {
                    var correctedItem = new BaseItemDto
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Type = item.Type,
                        IndexNumber = item.IndexNumber ?? episode.IndexNumber,
                        ParentIndexNumber = item.ParentIndexNumber ?? episode.ParentIndexNumber,
                        SeriesName = string.IsNullOrEmpty(item.SeriesName) ? series.Name : item.SeriesName,

                        // Simkl wants the *show* year for show identification, not the episode's.
                        ProductionYear = series.ProductionYear,

                        // The episode's own ids; the series ids travel separately so they end up
                        // on the show rather than on the episode.
                        ProviderIds = CopyProviderIds(episode.ProviderIds) ?? new Dictionary<string, string>(),
                        RunTimeTicks = item.RunTimeTicks
                    };

                    return new ScrobbleTarget(correctedItem, CopyProviderIds(series.ProviderIds));
                }

                _logger.LogDebug(
                    "Could not resolve a parent series for episode {ItemName}; scrobbling with the metadata Jellyfin reported",
                    item.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to resolve series metadata for {ItemName}; scrobbling with the metadata Jellyfin reported",
                    item.Name);
            }

            return new ScrobbleTarget(item, null);
        }

        private static Dictionary<string, string>? CopyProviderIds(Dictionary<string, string>? providerIds)
        {
            if (providerIds == null || providerIds.Count == 0)
            {
                return null;
            }

            return providerIds.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Evicts sessions that have been idle long enough that they cannot belong to a live
        /// playback. Trackers are only ever removed here, never disposed, so a handler still
        /// holding one keeps working against a detached copy instead of faulting.
        /// </summary>
        private void PruneIdleSessions(DateTime nowUtc)
        {
            if (_sessions.Count <= PruneThreshold)
            {
                return;
            }

            var cutoff = nowUtc - SessionRetention;
            foreach (var entry in _sessions)
            {
                if (entry.Value.LastEventUtc < cutoff)
                {
                    _sessions.TryRemove(entry.Key, out _);
                }
            }
        }
    }
}
