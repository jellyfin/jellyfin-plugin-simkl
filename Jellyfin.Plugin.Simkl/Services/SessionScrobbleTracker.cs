using System;
using System.Threading;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Per-session scrobble bookkeeping.
    /// </summary>
    /// <remarks>
    /// A Jellyfin session id is stable per device/client and is reused across playbacks, so the
    /// session id alone cannot tell one playback from the next. The tracked item id and the
    /// last-seen timestamp are what make that distinction, which is why both live here.
    /// </remarks>
    internal sealed class SessionScrobbleTracker
    {
        private long _lastEventTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionScrobbleTracker"/> class.
        /// </summary>
        /// <param name="itemId">The item being played.</param>
        /// <param name="nowUtc">The current UTC time.</param>
        public SessionScrobbleTracker(Guid itemId, DateTime nowUtc)
        {
            Gate = new SemaphoreSlim(1, 1);
            LastEventUtc = nowUtc;
            ResetFor(itemId);
        }

        /// <summary>
        /// Gets the semaphore serialising scrobble work for this session.
        /// </summary>
        /// <remarks>
        /// Deliberately never disposed. An event handler can still hold a reference after the
        /// tracker has been evicted, and disposing it from the event path threw
        /// ObjectDisposedException out of WaitAsync/Release. A SemaphoreSlim that never exposes
        /// AvailableWaitHandle owns no unmanaged resources, so leaving it to the GC is safe.
        /// </remarks>
        public SemaphoreSlim Gate { get; }

        /// <summary>
        /// Gets the id of the item this state applies to.
        /// </summary>
        public Guid ItemId { get; private set; }

        /// <summary>
        /// Gets or sets the last state successfully reported to Simkl.
        /// </summary>
        public SessionScrobbleState State { get; set; }

        /// <summary>
        /// Gets or sets the earliest time the next call may be attempted.
        /// </summary>
        public DateTime NextAttemptUtc { get; set; }

        /// <summary>
        /// Gets or sets the time Simkl last accepted a call for the current item.
        /// </summary>
        public DateTime LastSuccessUtc { get; set; }

        /// <summary>
        /// Gets or sets the number of consecutive failed attempts for the current item.
        /// </summary>
        public int ConsecutiveFailures { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the current item has been given up on.
        /// </summary>
        public bool Abandoned { get; set; }

        /// <summary>
        /// Gets or sets the progress the last playback event carried, whether or not it became a call to Simkl.
        /// </summary>
        /// <remarks>
        /// Stands in when the stop event carries no position of its own, which is what keeps a finished playback from
        /// being lost because its client went quiet before reporting one. Recorded from the event rather than from an
        /// accepted call, because calls are deliberately sparse: an accepted one can be ten minutes behind.
        /// </remarks>
        public float? LastPercentage { get; set; }

        /// <summary>
        /// Gets or sets the position Simkl last accepted for the current item, in ticks.
        /// </summary>
        /// <remarks>
        /// With <see cref="LastSuccessUtc"/> this says where playback should have reached by now, which is what
        /// tells a seek apart from ordinary advance: the session reports positions, never the fact that the user
        /// jumped.
        /// </remarks>
        public long? LastSentPositionTicks { get; set; }

        /// <summary>
        /// Gets or sets the position the previous playback event carried, in ticks.
        /// </summary>
        /// <remarks>
        /// Says whether the position is still moving under the user's hand. Dragging the scrub bar produces a
        /// burst of wildly different positions, and only the one they let go on is worth telling Simkl about.
        /// </remarks>
        public long? LastEventPositionTicks { get; set; }

        /// <summary>
        /// Gets or sets when the position last jumped, or <c>null</c> when it is not being moved.
        /// </summary>
        /// <remarks>
        /// Pushed out by every further jump, so a run of them reports once, where the user stopped. Somebody
        /// fast forwarding rests on a position for a second or two on the way past, and those are not places
        /// they watched.
        /// </remarks>
        public DateTime? SeekPendingUtc { get; set; }

        /// <summary>
        /// Gets or sets what Simkl's file search identified for the current item, when it could not match the ids.
        /// </summary>
        /// <remarks>
        /// Kept for the rest of the playback so the 404 and the search behind it happen once rather than on every
        /// event.
        /// </remarks>
        public ScrobbleRequest? Resolved { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether neither the ids nor the file name mean anything to Simkl.
        /// </summary>
        public bool Unmatched { get; set; }

        /// <summary>
        /// Gets or sets the item whose stop this session last reported. Kept across playbacks.
        /// </summary>
        /// <remarks>
        /// Jellyfin reports a stop twice, and the second copy can land after the next item has started. This is
        /// what tells that copy, which is dropped, from the first report of a stop the next start overtook, which
        /// is the only record of that watch there is.
        /// </remarks>
        public Guid? StoppedItemId { get; set; }

        /// <summary>
        /// Gets or sets the time of the last playback event seen for this session.
        /// </summary>
        /// <remarks>
        /// Read from the prune path without holding <see cref="Gate"/>, hence the interlocked
        /// access.
        /// </remarks>
        public DateTime LastEventUtc
        {
            get => new DateTime(Interlocked.Read(ref _lastEventTicks), DateTimeKind.Utc);
            set => Interlocked.Exchange(ref _lastEventTicks, value.Ticks);
        }

        /// <summary>
        /// Rebinds this tracker to a new playback, clearing all progress state.
        /// </summary>
        /// <param name="itemId">The item now being played.</param>
        public void ResetFor(Guid itemId)
        {
            ItemId = itemId;
            State = SessionScrobbleState.NotStarted;
            NextAttemptUtc = DateTime.MinValue;
            LastSuccessUtc = DateTime.MinValue;
            ConsecutiveFailures = 0;
            Abandoned = false;
            LastPercentage = null;
            LastSentPositionTicks = null;
            LastEventPositionTicks = null;
            SeekPendingUtc = null;
            Resolved = null;
            Unmatched = false;
        }
    }
}
