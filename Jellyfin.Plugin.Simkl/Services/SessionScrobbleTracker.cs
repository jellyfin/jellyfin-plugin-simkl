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
        }
    }
}
