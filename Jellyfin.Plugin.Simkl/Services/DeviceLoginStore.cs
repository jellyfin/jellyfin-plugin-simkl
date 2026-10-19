using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// The device logins that are waiting to be approved, one per Jellyfin user.
    /// </summary>
    /// <remarks>
    /// In memory on purpose. A device code is good for fifteen minutes and is worth an access token to whoever holds
    /// it, so it is never written to the configuration file and never handed to the browser: the page drives the
    /// login by user id and the server keeps the credential. A restart in the middle of a login loses it, which
    /// costs the user one click.
    /// </remarks>
    public class DeviceLoginStore
    {
        private readonly ConcurrentDictionary<Guid, PendingLogin> _pending = new ConcurrentDictionary<Guid, PendingLogin>();

        /// <summary>
        /// Remembers a device code as the login attempt of one user, replacing whatever that user had going.
        /// </summary>
        /// <param name="userId">The Jellyfin user signing in.</param>
        /// <param name="deviceCode">The device code to poll with.</param>
        /// <param name="expiresAt">When the code runs out, in UTC.</param>
        /// <param name="interval">The shortest polling interval, in seconds.</param>
        public void Start(Guid userId, string deviceCode, DateTime expiresAt, int interval)
        {
            Prune();
            _pending[userId] = new PendingLogin(deviceCode, expiresAt, interval);
        }

        /// <summary>
        /// Reads the login a user has going, if it has not run out.
        /// </summary>
        /// <param name="userId">The Jellyfin user.</param>
        /// <returns>The pending login, or null.</returns>
        public PendingLogin? Get(Guid userId)
        {
            if (!_pending.TryGetValue(userId, out var pending))
            {
                return null;
            }

            if (pending.ExpiresAt <= DateTime.UtcNow)
            {
                Cancel(userId);
                return null;
            }

            return pending;
        }

        /// <summary>
        /// Forgets whatever login a user had going, or only the given attempt when one is named.
        /// </summary>
        /// <remarks>
        /// A poll is on the wire for seconds, and a cancel, a logout or a second sign in may land while it is.
        /// Naming the attempt it started with lets a late answer drop that one and never the one that replaced it,
        /// and the answer says whether it was still the current attempt at all. The compare and remove is one
        /// atomic step, by reference.
        /// </remarks>
        /// <param name="userId">The Jellyfin user.</param>
        /// <param name="attempt">The attempt to drop, or null for whatever is there.</param>
        /// <returns><c>true</c> when an attempt was dropped.</returns>
        public bool Cancel(Guid userId, PendingLogin? attempt = null)
        {
            return attempt == null
                ? _pending.TryRemove(userId, out _)
                : _pending.TryRemove(new KeyValuePair<Guid, PendingLogin>(userId, attempt));
        }

        // nothing else ever removes an attempt the user simply walked away from
        private void Prune()
        {
            var now = DateTime.UtcNow;
            foreach (var stale in _pending.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToList())
            {
                _pending.TryRemove(stale, out _);
            }
        }

        /// <summary>
        /// One login waiting to be approved.
        /// </summary>
        public sealed class PendingLogin
        {
            private int _failures;

            /// <summary>
            /// Initializes a new instance of the <see cref="PendingLogin"/> class.
            /// </summary>
            /// <param name="deviceCode">The device code to poll with.</param>
            /// <param name="expiresAt">When the code runs out, in UTC.</param>
            /// <param name="interval">The shortest polling interval, in seconds.</param>
            public PendingLogin(string deviceCode, DateTime expiresAt, int interval)
            {
                DeviceCode = deviceCode;
                ExpiresAt = expiresAt;
                Interval = interval;
            }

            /// <summary>
            /// Gets the device code to poll with.
            /// </summary>
            public string DeviceCode { get; }

            /// <summary>
            /// Gets when the code runs out, in UTC.
            /// </summary>
            public DateTime ExpiresAt { get; }

            /// <summary>
            /// Gets the shortest polling interval, in seconds.
            /// </summary>
            public int Interval { get; }

            /// <summary>
            /// Gets how many seconds are left before the code runs out.
            /// </summary>
            /// <returns>The seconds left, never below zero.</returns>
            public int SecondsLeft()
            {
                return (int)Math.Max(0, Math.Round((ExpiresAt - DateTime.UtcNow).TotalSeconds));
            }

            /// <summary>
            /// Counts one answer that said nothing about the login, so a permanently unreachable Simkl is given up
            /// on rather than polled until the code runs out.
            /// </summary>
            /// <returns>How many have now happened in a row.</returns>
            public int RecordFailure()
            {
                return Interlocked.Increment(ref _failures);
            }

            /// <summary>
            /// Forgets the run of failures, called whenever Simkl answers about the login again.
            /// </summary>
            public void ClearFailures()
            {
                Interlocked.Exchange(ref _failures, 0);
            }
        }
    }
}
