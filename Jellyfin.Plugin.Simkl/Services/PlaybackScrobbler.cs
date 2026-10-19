using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
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
        /// Minimum time between two scrobble calls for the same session. A backstop against duplicate events from
        /// a single client.
        /// </summary>
        /// <remarks>
        /// Short on purpose. What reaches it is already discrete user actions: a start, a pause, a settled seek, a
        /// stop. Burst control belongs to the seek debounce, which reports where the user let go rather than every
        /// position they dragged over, so a wide floor here only delays calls that were worth making.
        /// </remarks>
        private static readonly TimeSpan MinSessionCallInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Minimum time between two scrobble calls for the same user, across all of their
        /// sessions. This is the actual ceiling on how hard one account can hit Simkl; the
        /// per-session interval alone would multiply by the number of devices in use.
        /// </summary>
        /// <remarks>
        /// Simkl documents no numeric rate limit (only that HTTP 429 exists), so this is a self-imposed courtesy
        /// limit, and the real protection is the cooldown taken from a 429 or from the account lock behind a 400.
        /// It cannot be wide: the stop of one episode and the start of the next arrive a tenth of a second apart
        /// when a series plays on, and a wide floor there is felt directly as the next episode not showing up as
        /// watching now.
        /// </remarks>
        private static readonly TimeSpan MinUserCallInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How long to hold off all calls for a user after an HTTP 429 that carried no
        /// Retry-After header.
        /// </summary>
        private static readonly TimeSpan DefaultRateLimitCooldown = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How long to hold off after Simkl answers an HTTP 400, which is how it reports a call that arrived while
        /// its own per-account scrobble lock was held.
        /// </summary>
        /// <remarks>
        /// That lock is about 20 seconds wide, and a 400 never carries a Retry-After, so the wait is this fixed
        /// value. It has to stay below <see cref="MaxStopWait"/>: a terminal stop landing inside the window waits
        /// the remainder out, where a longer hold would make it give up and lose the watch entirely.
        /// </remarks>
        private static readonly TimeSpan AccountLockCooldown = TimeSpan.FromSeconds(21);

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

        /// <summary>
        /// How far the reported position may be from where playback should have reached before it counts as a seek
        /// rather than ordinary advance.
        /// </summary>
        /// <remarks>
        /// Wide enough to absorb a progress event arriving late, a client whose clock runs slightly differently and
        /// the buffering pause at the start of a stream, and still far narrower than any jump a user makes on
        /// purpose.
        /// </remarks>
        private static readonly long SeekToleranceTicks = 30 * TimeSpan.TicksPerSecond;

        /// <summary>
        /// How far two consecutive events may differ before the position counts as still being dragged rather
        /// than simply advancing.
        /// </summary>
        /// <remarks>
        /// Comfortably above the widest ordinary gap between progress events, so normal playback never looks like
        /// a drag, and far below any jump worth making by hand.
        /// </remarks>
        private static readonly long ScrubSettleTicks = 15 * TimeSpan.TicksPerSecond;

        /// <summary>
        /// How long the position must stop jumping before a seek is reported.
        /// </summary>
        /// <remarks>
        /// Wide enough to swallow a run of skips through an opening or a recap, where each landing is held for a
        /// second or two and none of them is somewhere the user watched. It is also the delay before a single
        /// deliberate jump shows up on Simkl, which is what stops it being wider.
        /// </remarks>
        private static readonly TimeSpan SeekHold = TimeSpan.FromSeconds(3);

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
        /// Cancels work in flight when the server stops, so a stop retry or a file search does not outlive it.
        /// </summary>
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();

        /// <summary>
        /// Reminds users still signed in through the retired pin flow to reconnect.
        /// </summary>
        private readonly ReloginNudge _relogin;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackScrobbler"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PlaybackScrobbler}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="relogin">Instance of the <see cref="ReloginNudge"/>.</param>
        public PlaybackScrobbler(
            ISessionManager sessionManager,
            ILogger<PlaybackScrobbler> logger,
            SimklApi simklApi,
            ILibraryManager libraryManager,
            ReloginNudge relogin)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _simklApi = simklApi;
            _libraryManager = libraryManager;
            _relogin = relogin;
            _sessions = new ConcurrentDictionary<string, SessionScrobbleTracker>(StringComparer.Ordinal);
            _userRateLimiter = new RateLimiter<Guid>(MinUserCallInterval);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _sessions.Clear();

            // a stop retry waiting out a rate limit, and the file search behind a 404, end with the server
            _shutdown.Cancel();
            return Task.CompletedTask;
        }

        private static bool CanStartScrobbling(UserConfig config, PlaybackProgressEventArgs playbackProgress)
        {
            var runtime = playbackProgress.MediaInfo?.RunTimeTicks;

            // Must have a known runtime above the configured minimum length. A second is 10,000,000 ticks, not
            // 10,000: the old figure made the default five minutes mean three tenths of a second, so the setting
            // never excluded anything and the played mirror, which computes this correctly, disagreed with the
            // scrobbler about the same item.
            if (!runtime.HasValue || runtime.Value < 60L * TimeSpan.TicksPerSecond * config.MinLength)
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

        /// <summary>
        /// Tells whether the position reported now is further from where playback should have reached than normal
        /// advance can explain.
        /// </summary>
        /// <remarks>
        /// Only meaningful while playing. A paused session keeps reporting the same position while the clock moves
        /// on, which would look like an ever growing jump, so the caller checks the state first.
        /// </remarks>
        /// <param name="tracker">The session being tracked.</param>
        /// <param name="e">The playback event.</param>
        /// <param name="nowUtc">The current UTC time.</param>
        /// <returns><c>true</c> when the user has seeked.</returns>
        private static bool Jumped(SessionScrobbleTracker tracker, PlaybackProgressEventArgs e, DateTime nowUtc)
        {
            var position = e.PlaybackPositionTicks;
            if (position == null || tracker.LastSentPositionTicks == null || tracker.LastSuccessUtc == DateTime.MinValue)
            {
                return false;
            }

            var expected = tracker.LastSentPositionTicks.Value + (nowUtc - tracker.LastSuccessUtc).Ticks;
            return Math.Abs(position.Value - expected) > SeekToleranceTicks;
        }

        /// <summary>
        /// Tells whether the position has stopped moving under the user's hand.
        /// </summary>
        /// <remarks>
        /// Two consecutive events an ordinary interval apart can only differ by about that interval of playback.
        /// A far larger step means the scrub bar is still being dragged, and the position now is one the user is
        /// passing over rather than the one they will land on.
        /// </remarks>
        /// <param name="previousPosition">The position the previous event carried.</param>
        /// <param name="position">The position this event carries.</param>
        /// <returns><c>true</c> when the position is worth reporting.</returns>
        private static bool Settled(long? previousPosition, long? position)
        {
            if (previousPosition == null || position == null)
            {
                return true;
            }

            return Math.Abs(position.Value - previousPosition.Value) <= ScrubSettleTicks;
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
        private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            Observe(HandleStartAsync(e), "playback start");
        }

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

        /// <summary>
        /// Rebinds the tracker at the one moment a new playback is unambiguous.
        /// </summary>
        /// <remarks>
        /// A Jellyfin session id is stable per client and is reused, so playing the same item again on the same
        /// client is indistinguishable from a trailing progress event of the playback that just ended. Without
        /// this, the Stopped the previous playback left behind swallows the replay whole: no start, no stop,
        /// nothing logged. Inferring it from a quiet period cannot work either, because the replay's own events
        /// keep the session looking busy.
        /// </remarks>
        /// <param name="e">The playback event.</param>
        /// <returns>A task.</returns>
        private async Task HandleStartAsync(PlaybackProgressEventArgs e)
        {
            var tracker = GetTracker(e.Session, e.MediaInfo, DateTime.UtcNow);
            if (tracker == null)
            {
                return;
            }

            await tracker.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                // The start of the next episode lands a tenth of a second behind the stop of the last one, inside
                // the per-user floor. That short a wait is taken here rather than leaving the start to the next
                // progress event, which on some clients is ten seconds away; a cooldown from a 400 or a 429 is far
                // longer and is left to the events that follow.
                var wait = (_userRateLimiter.GetNextAllowed(e.Session.UserId) ?? DateTime.MinValue) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero && wait <= MinUserCallInterval)
                {
                    // a millisecond past the floor: the delay is only as fine as a millisecond and lands a hair short of it
                    await Task.Delay(wait + TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
                }

                // the clock is read once the gate is held: a start that queued behind the previous item's stop would
                // otherwise be measured against the rate limit as of before it waited
                var now = DateTime.UtcNow;
                tracker.ResetFor(e.MediaInfo!.Id);
                tracker.LastEventUtc = now;

                // and report it now rather than leaving it to the first progress event: those arrive seconds
                // apart, and "watching now" appearing only once one has is the difference between Simkl showing
                // what is on screen and showing nothing at all
                await HandleStateChangeAsync(tracker, e, now).ConfigureAwait(false);
            }
            finally
            {
                tracker.Gate.Release();
            }
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

            // playback is the one moment a user is provably in front of a client that can show a message; the nudge
            // itself decides whether this user is due one, and it never holds up the scrobble
            if (!string.IsNullOrEmpty(e.Session.Id))
            {
                Observe(_relogin.Remind(userConfig, e.Session.Id, _shutdown.Token), "relogin reminder");
            }

            if (!CanStartScrobbling(userConfig, e))
            {
                return;
            }

            // What the client last said, kept whether or not this event becomes a call. The stop falls back to it
            // when it carries no position of its own, and a figure only written when Simkl accepted something
            // would be up to ProgressRefreshInterval out of date by then.
            var hasPosition = TryGetPercentageWatched(e, out var reportedNow);
            if (hasPosition)
            {
                tracker.LastPercentage = reportedNow;
            }

            // Every jump pushes the wait out again, so a run of them is reported once, at the position the user
            // stopped on. Fast forwarding rests somewhere for a second or two on the way past, and telling Simkl
            // about those would say the user watched from places they were only passing through.
            //
            // A position that has drifted from where Simkl believes playback is opens the wait as well: a run of
            // short skips moves in steps too small to look like a jump on their own, and adds up to one all the same.
            var previousPosition = tracker.LastEventPositionTicks;
            tracker.LastEventPositionTicks = e.PlaybackPositionTicks;
            if (!Settled(previousPosition, e.PlaybackPositionTicks)
                || (tracker.SeekPendingUtc == null && tracker.State == SessionScrobbleState.Started && Jumped(tracker, e, nowUtc)))
            {
                tracker.SeekPendingUtc = nowUtc;
            }

            // nothing can be sent without a position, so the user's call slot is not spent on a call that cannot go
            // out; the first event that carries one makes it
            if (!hasPosition)
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

                // A seek is a user action Simkl should hear about, but nothing announces one: the session only
                // ever reports a position. Since the last accepted call, playback can have moved on by the time
                // that has passed and no more, so a position anywhere else is a jump. Without this a skip forward
                // is invisible until the ten minute refresh, which is what makes a jump to the middle of a film
                // leave Simkl showing the old position.
                //
                // Held until the jumping has stopped for SeekHold, so a run of skips reports once, where the user
                // came to rest, rather than at every position they paused on for a second on the way there.
                var jumped = desiredState == SessionScrobbleState.Started
                             && tracker.SeekPendingUtc != null
                             && nowUtc - tracker.SeekPendingUtc.Value >= SeekHold
                             && Jumped(tracker, e, nowUtc);

                if (!dueForRefresh && !jumped)
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
            var outcome = await SendScrobbleAsync(tracker, action, e, userConfig).ConfigureAwait(false);

            RecordAttempt(tracker, outcome, desiredState, e);
        }

        private async Task HandleStopAsync(SessionScrobbleTracker tracker, PlaybackStopEventArgs e, DateTime nowUtc)
        {
            var progressArgs = CreateProgressArgsFromStop(e);

            // A stop for something other than what this session is playing now belongs to a playback that has
            // already been handed over. Jellyfin reports the same stop twice, and the second copy can arrive after
            // the next item has started: rebinding the tracker backwards would send the finished item's stop a
            // second time and take the new item's place in the queue, which is what delayed the next episode.
            //
            // The first copy can land that late too, when the server gets to the next item's start ahead of it,
            // and then it is the only record of the watch there is. It goes out on a tracker of its own, so the
            // item now playing keeps its place; Simkl closes that item's "now watching" along with the stop, so
            // its start is sent again on the next event.
            var itemId = e.MediaInfo!.Id;
            if (tracker.ItemId != itemId)
            {
                if (tracker.StoppedItemId == itemId)
                {
                    _logger.LogDebug(
                        "Ignoring a Simkl stop for {Name}: this session has moved on to another playback",
                        e.MediaInfo.Name);
                    return;
                }

                tracker.StoppedItemId = itemId;
                await HandleStopAsync(new SessionScrobbleTracker(itemId, nowUtc), e, nowUtc).ConfigureAwait(false);
                if (tracker.State is SessionScrobbleState.Started or SessionScrobbleState.Paused)
                {
                    tracker.State = SessionScrobbleState.NotStarted;
                }

                return;
            }

            tracker.StoppedItemId = itemId;
            tracker.LastEventUtc = nowUtc;

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

            // The stop always goes out for an eligible item, whether or not a start was ever sent: it is what closes
            // an open "now watching" entry, and Simkl's own clock is what decides whether the watch counts. The
            // plugin deliberately holds no percentage setting of its own.
            //
            // A client that reports no final position leaves nothing to send, so the last position this playback did
            // report stands in; a playback the server itself calls complete counts as the whole item. Without that
            // fallback a finished episode whose client went quiet was simply lost.
            float? percentage = TryGetPercentageWatched(progressArgs, out var reported)
                ? reported
                : e.PlayedToCompletion ? 100f : tracker.LastPercentage;

            if (percentage == null)
            {
                if (hadOpenScrobble)
                {
                    _logger.LogWarning(
                        "{Name} stopped without any reported position, so Simkl cannot be told where it ended",
                        e.MediaInfo!.Name);
                }

                tracker.State = SessionScrobbleState.Stopped;
                return;
            }

            if (percentage < SimklWatchedThreshold)
            {
                _logger.LogInformation(
                    "{Name} stopped at {Progress:F1}%, below the {Threshold}% Simkl needs to mark it watched; the position is saved instead",
                    e.MediaInfo!.Name,
                    percentage,
                    SimklWatchedThreshold);
            }

            // Not throttled and retried in place: this is the call that actually records the
            // watch, and there is no later playback event to retry it on.
            await SendStopWithRetryAsync(tracker, progressArgs, userConfig, percentage.Value).ConfigureAwait(false);

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

                    // where Simkl now believes playback is, which is what the next event is measured against to
                    // tell a seek from ordinary advance
                    tracker.LastSentPositionTicks = e.PlaybackPositionTicks;

                    // cleared only once it has actually been reported, so a call the rate limiter deferred or
                    // Simkl refused is tried again on the next event rather than forgotten
                    tracker.SeekPendingUtc = null;
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

        private async Task<bool> SendStopWithRetryAsync(SessionScrobbleTracker tracker, PlaybackProgressEventArgs e, UserConfig userConfig, float percentage)
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

                var outcome = await SendScrobbleAsync(tracker, ScrobbleAction.Stop, e, userConfig, percentage).ConfigureAwait(false);

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

        private async Task<ScrobbleOutcome> SendScrobbleAsync(
            SessionScrobbleTracker tracker,
            ScrobbleAction action,
            PlaybackProgressEventArgs e,
            UserConfig userConfig,
            float? forcedPercentage = null)
        {
            float percentageWatched;
            if (forcedPercentage.HasValue)
            {
                percentageWatched = forcedPercentage.Value;
            }
            else if (!TryGetPercentageWatched(e, out percentageWatched))
            {
                return ScrobbleOutcome.Skipped;
            }

            // an item Simkl knows by neither its ids nor its file name answers the same way every time
            if (tracker.Unmatched)
            {
                return ScrobbleOutcome.Skipped;
            }

            var itemName = e.MediaInfo!.Name;
            var userName = e.Session.UserName;
            var verb = action switch
            {
                ScrobbleAction.Start => "start",
                ScrobbleAction.Pause => "pause",
                _ => "stop"
            };

            try
            {
                var target = await GetScrobbleTargetAsync(e.MediaInfo).ConfigureAwait(false);

                // what the file search identified earlier in this playback, if anything, otherwise the ids the
                // library holds
                var request = tracker.Resolved == null
                    ? SimklApi.BuildRequest(target.Item, percentageWatched, target.SeriesProviderIds)
                    : WithProgress(tracker.Resolved, percentageWatched);
                if (request == null)
                {
                    return ScrobbleOutcome.Skipped;
                }

                _logger.LogDebug(
                    "Sending scrobble {Action} for {Name} ({Progress:F1}%) for {UserName}",
                    action,
                    itemName,
                    percentageWatched,
                    userName);

                var (status, response, retryAfter) = await _simklApi
                    .Scrobble(verb, request, userConfig, _shutdown.Token)
                    .ConfigureAwait(false);

                // Simkl could not place the ids. Its own file search is the fallback, and what it identifies is kept
                // for the rest of the playback so this happens once.
                if (status == HttpStatusCode.NotFound)
                {
                    var resolved = await ResolveByFileNameAsync(tracker, verb, e, target.Item, userConfig, percentageWatched).ConfigureAwait(false);
                    if (resolved == null)
                    {
                        return ScrobbleOutcome.Permanent;
                    }

                    (status, response, retryAfter) = resolved.Value;
                }

                if (status == HttpStatusCode.Conflict)
                {
                    // already recorded within the hour; there is nothing further to send for it
                    _logger.LogInformation("Simkl already has {Name} as watched within the last hour", itemName);
                    return ScrobbleOutcome.Succeeded;
                }

                if (status == HttpStatusCode.TooManyRequests || status == HttpStatusCode.BadRequest)
                {
                    // Simkl serialises an account's scrobbles behind a lock about 20 seconds wide and answers a call
                    // that arrives inside it with a 400 rather than a 429. The two are held off for different
                    // lengths of time: the lock clears on its own in about twenty seconds, where a real 429 is the
                    // daily quota and deserves the longer wait.
                    var cooldown = retryAfter
                        ?? (status == HttpStatusCode.BadRequest ? AccountLockCooldown : DefaultRateLimitCooldown);
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

                if ((int)status >= 400)
                {
                    _logger.LogWarning("Scrobble {Action} of {Name} failed with HTTP {StatusCode}", action, itemName, (int)status);
                    return (int)status >= 500 ? ScrobbleOutcome.Transient : ScrobbleOutcome.Permanent;
                }

                if (response == null)
                {
                    _logger.LogWarning("Simkl returned an empty body for scrobble {Action} of {Name}", action, itemName);
                    return ScrobbleOutcome.Transient;
                }

                // a refusal Simkl reports on an HTTP 200
                if (!string.IsNullOrEmpty(response.Error))
                {
                    SimklErrorHandler.LogError(_logger, response.Error, target.Item, request);
                    return SimklErrorHandler.IsTransient(response.Error)
                        ? ScrobbleOutcome.Transient
                        : ScrobbleOutcome.Permanent;
                }

                if (string.Equals(response.RewatchStatus, "pro_required", StringComparison.Ordinal))
                {
                    DisableRewatch(userConfig, e.Session.UserId, userName);
                }

                _logger.LogInformation(
                    "Scrobble {Action} accepted for {Name} by {UserName} at {Progress:F1}% (Simkl action: {ServerAction}{Rewatch})",
                    action,
                    itemName,
                    userName,
                    percentageWatched,
                    response.Action,
                    string.IsNullOrEmpty(response.RewatchStatus) ? string.Empty : ", rewatch " + response.RewatchStatus);
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
                        RunTimeTicks = item.RunTimeTicks,

                        // carried over because identification by file name is what happens when Simkl cannot place
                        // the ids, and it has nothing to search for without this
                        Path = item.Path
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
        /// Tells whether this user wants this item reported at all.
        /// </summary>
        /// <remarks>
        /// The entity counterpart of <c>CanStartScrobbling</c>, for the played/unplayed mirror, which is handed a
        /// library item rather than a playback event.
        /// </remarks>
        /// <param name="config">The settings of the user.</param>
        /// <param name="item">The library item.</param>
        /// <returns><c>true</c> when it is a movie or an episode the user scrobbles and it is long enough.</returns>
        public static bool CanBeScrobbled(UserConfig config, BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(item);

            // an item without a runtime keeps its place here: nothing can be reported for it anyway, since there is
            // no progress to send
            if (item.RunTimeTicks < 60L * TimeSpan.TicksPerSecond * config.MinLength)
            {
                return false;
            }

            return item switch
            {
                Movie => config.ScrobbleMovies,
                Episode => config.ScrobbleShows,
                _ => false
            };
        }

        // the identification the file search found, carrying the progress of the event at hand
        private static ScrobbleRequest WithProgress(ScrobbleRequest resolved, float progress)
        {
            return new ScrobbleRequest
            {
                Progress = progress,
                Movie = resolved.Movie,
                Show = resolved.Show,
                Episode = resolved.Episode
            };
        }

        // Simkl matched nothing by ids. Its file search is asked by whole path and then by file name alone, and what
        // it identifies is kept on the tracker so the rest of the playback needs neither the 404 nor the search.
        private async Task<(HttpStatusCode Status, ScrobbleResponse? Response, TimeSpan? RetryAfter)?> ResolveByFileNameAsync(
            SessionScrobbleTracker tracker,
            string verb,
            PlaybackProgressEventArgs e,
            BaseItemDto item,
            UserConfig userConfig,
            float percentage)
        {
            _logger.LogInformation("Simkl couldn't match ids for {Name}, identifying by file name", item.Name);

            foreach (var fullPath in new[] { true, false })
            {
                var resolved = await _simklApi.BuildRequestFromFile(item, fullPath, percentage, userConfig, _shutdown.Token).ConfigureAwait(false);
                if (resolved == null)
                {
                    continue;
                }

                var answer = await _simklApi.Scrobble(verb, resolved, userConfig, _shutdown.Token).ConfigureAwait(false);
                if (answer.Status == HttpStatusCode.NotFound)
                {
                    continue;
                }

                tracker.Resolved = resolved;
                return answer;
            }

            tracker.Unmatched = true;
            _logger.LogInformation("Simkl doesn't know {Name}; it is not reported again during this playback", item.Name);
            return null;
        }

        // Simkl answers pro_required when the account behind the token is no longer Pro or VIP. The flag is switched
        // off in the stored settings so later stops go out without it, and on the snapshot in hand so this playback's
        // do too. Only the settings of the token Simkl actually answered for are touched.
        private void DisableRewatch(UserConfig userConfig, Guid userId, string? userName)
        {
            if (SimklPlugin.Instance?.Configuration.DisableRewatch(userId, userConfig.UserToken) == true)
            {
                _logger.LogInformation("Simkl rewatches need a Pro or VIP plan; switched them off for {UserName}", userName);
            }

            userConfig.AllowRewatch = false;
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
