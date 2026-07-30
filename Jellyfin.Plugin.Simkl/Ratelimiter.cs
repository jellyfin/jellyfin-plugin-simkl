using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Simkl
{
    /// <summary>
    /// Provides a simple time-based rate limiter for keyed operations.
    /// Each key can execute an action at most once per configured interval.
    /// </summary>
    /// <typeparam name="TKey">The key used to track rate limits (must be non-null).</typeparam>
    public class RateLimiter<TKey>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, DateTime> _nextAllowed = new ConcurrentDictionary<TKey, DateTime>();
        private readonly TimeSpan _interval;

        /// <summary>
        /// Initializes a new instance of the <see cref="RateLimiter{TKey}"/> class.
        /// </summary>
        /// <param name="interval">Minimum time between executions for a given key.</param>
        public RateLimiter(TimeSpan interval)
        {
            _interval = interval;
        }

        /// <summary>
        /// Determines whether the specified key is allowed to execute at the given time.
        /// </summary>
        /// <param name="key">The key representing the caller or context.</param>
        /// <param name="now">The current timestamp (typically UTC).</param>
        /// <returns><c>true</c> if execution is allowed; otherwise, <c>false</c>.</returns>
        public bool CanExecute(TKey key, DateTime now)
        {
            return !_nextAllowed.TryGetValue(key, out var next) || now >= next;
        }

        /// <summary>
        /// Marks the specified key as having executed at the given time,
        /// updating the next allowed execution time.
        /// </summary>
        /// <param name="key">The key representing the caller or context.</param>
        /// <param name="now">The current timestamp (typically UTC).</param>
        public void MarkExecuted(TKey key, DateTime now)
        {
            Defer(key, now + _interval);
        }

        /// <summary>
        /// Blocks the specified key until at least the given time.
        /// </summary>
        /// <param name="key">The key representing the caller or context.</param>
        /// <param name="until">The earliest time the key may execute again.</param>
        /// <remarks>
        /// Never shortens an existing deferral, so a long server-imposed cooldown (an HTTP 429
        /// with a Retry-After) cannot be cleared by a routine <see cref="MarkExecuted"/>.
        /// </remarks>
        public void Defer(TKey key, DateTime until)
        {
            _nextAllowed.AddOrUpdate(key, until, (_, existing) => existing > until ? existing : until);
        }

        /// <summary>
        /// Gets the next allowed execution time for the specified key, if any.
        /// </summary>
        /// <param name="key">The key representing the caller or context.</param>
        /// <returns>
        /// The next allowed <see cref="DateTime"/> if the key is tracked; otherwise, <c>null</c>.
        /// </returns>
        public DateTime? GetNextAllowed(TKey key)
        {
            return _nextAllowed.TryGetValue(key, out var next) ? next : null;
        }

        /// <summary>
        /// Removes the specified key from the rate limiter, clearing its state.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        public void Clear(TKey key)
        {
            _nextAllowed.TryRemove(key, out _);
        }
    }
}