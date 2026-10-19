using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configure it.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        // Every server-side change to the stored configuration goes through a method on this class that takes this
        // lock, because SaveConfiguration() writes the whole document: two writers that each build their own array
        // would drop each other's change. The dashboard's own save replaces the configuration object wholesale, so
        // it holds the lock across both halves of that swap through ReplaceRunning, and PreserveServerState is what
        // carries the server-owned fields over inside it.
        private static readonly object SaveLock = new object();

        private UserConfig[] _userConfigs = Array.Empty<UserConfig>();

        /// <summary>
        /// Gets or sets the list of user configs. A deserializer that passes null leaves it empty.
        /// </summary>
        public UserConfig[] UserConfigs
        {
            get => _userConfigs;
            set => _userConfigs = value ?? Array.Empty<UserConfig>();
        }

        /// <summary>
        /// Carries the stored login of every user from the running configuration into one posted to
        /// /Plugins/{id}/Configuration.
        /// </summary>
        /// <remarks>
        /// The login is server owned end to end: it is written by the device flow, by a token renewal and by the
        /// logout route, and never by the page. So it is taken from the stored configuration unconditionally rather
        /// than only rescued when the post left it empty - a post carrying a token that renewal has since replaced
        /// would otherwise put the dead one back. The import bookkeeping needs no such rescue: it lives in its own
        /// file, see <see cref="Services.ImportStateStore"/>.
        /// </remarks>
        /// <param name="current">The configuration the plugin is running with.</param>
        /// <param name="incoming">The configuration that is about to replace it.</param>
        public static void PreserveServerState(PluginConfiguration current, PluginConfiguration incoming)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(incoming);
            if (ReferenceEquals(current, incoming))
            {
                return;
            }

            lock (SaveLock)
            {
                var merged = new List<UserConfig>(incoming.UserConfigs);
                foreach (var posted in merged)
                {
                    posted.CopyTokensFrom(current.GetByGuid(posted.Id));
                }

                // a user the post left out keeps their login, and their settings with it
                foreach (var stored in current.UserConfigs)
                {
                    if (!string.IsNullOrEmpty(stored.UserToken) && !merged.Exists(c => c.Id == stored.Id))
                    {
                        merged.Add(stored);
                    }
                }

                incoming.UserConfigs = merged.ToArray();
            }
        }

        /// <summary>
        /// Carries the stored logins over and installs the posted configuration as one step.
        /// </summary>
        /// <remarks>
        /// Preserving and then installing as two steps leaves a gap: a login, a renewal or a logout landing
        /// between them is written to the configuration object that is about to be thrown away. Holding the lock
        /// across both means a writer either finishes first, and is carried over, or waits and then writes to the
        /// configuration that is now live.
        /// </remarks>
        /// <param name="current">Reads the configuration the plugin is running with, under the lock.</param>
        /// <param name="incoming">The configuration that is about to replace it.</param>
        /// <param name="install">Makes <paramref name="incoming"/> the running configuration and writes it out.</param>
        public static void ReplaceRunning(Func<PluginConfiguration> current, PluginConfiguration incoming, Action install)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(install);
            lock (SaveLock)
            {
                // read here rather than taken as a value: one read outside the lock could be of a configuration
                // that another save replaced before this one took it, which is the gap this method exists to close
                PreserveServerState(current(), incoming);
                install();
            }
        }

        /// <summary>
        /// Get config by id.
        /// </summary>
        /// <param name="id">The user id.</param>
        /// <returns>Stored user config.</returns>
        public UserConfig? GetByGuid(Guid id)
        {
            return UserConfigs.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Stores the login the device flow just completed, creating the user's settings when the page never saved
        /// any.
        /// </summary>
        /// <param name="userId">The user id.</param>
        /// <param name="accessToken">The access token.</param>
        /// <param name="refreshToken">The refresh token.</param>
        /// <param name="expiresAt">When the access token expires, in UTC.</param>
        /// <param name="scope">The scope Simkl granted.</param>
        public void StoreLogin(Guid userId, string accessToken, string refreshToken, DateTime expiresAt, string scope)
        {
            lock (SaveLock)
            {
                var live = Live();
                var config = live.GetByGuid(userId);
                if (config == null)
                {
                    config = new UserConfig { Id = userId };
                    live.UserConfigs = live.UserConfigs.Append(config).ToArray();
                }

                config.UserToken = accessToken;
                config.RefreshToken = refreshToken;
                config.TokenExpiresAt = expiresAt;
                config.TokenScope = scope;
                config.LastReloginPromptAt = default;
                SimklPlugin.Instance?.SaveConfiguration();
            }
        }

        /// <summary>
        /// Writes a renewed access token over the one it replaces, wherever that one is stored.
        /// </summary>
        /// <remarks>
        /// By value, and over every user holding it: two Jellyfin users may have signed in to the same Simkl account
        /// and then share a token. Matching on the old value also means a renewal that lost a race to a fresh login
        /// finds nothing to write and leaves the new login alone.
        /// </remarks>
        /// <param name="oldAccessToken">The access token that was renewed.</param>
        /// <param name="accessToken">The access token Simkl answered with.</param>
        /// <param name="refreshToken">The refresh token to keep; Simkl reissues the same one.</param>
        /// <param name="expiresAt">When the new access token expires, in UTC.</param>
        /// <param name="scope">The scope Simkl granted.</param>
        /// <returns><c>true</c> when at least one user's settings were changed.</returns>
        public bool ReplaceToken(string oldAccessToken, string accessToken, string refreshToken, DateTime expiresAt, string scope)
        {
            lock (SaveLock)
            {
                var replaced = false;
                foreach (var config in Live().UserConfigs)
                {
                    if (!string.Equals(config.UserToken, oldAccessToken, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    config.UserToken = accessToken;
                    config.RefreshToken = refreshToken;
                    config.TokenExpiresAt = expiresAt;
                    config.TokenScope = scope;
                    replaced = true;
                }

                if (replaced)
                {
                    SimklPlugin.Instance?.SaveConfiguration();
                }

                return replaced;
            }
        }

        /// <summary>
        /// Claims the right to remind one user to sign in again, when they are due one.
        /// </summary>
        /// <remarks>
        /// The test and the stamp are one step under the lock, so of two sessions starting at the same moment
        /// exactly one is told to show the reminder.
        /// </remarks>
        /// <param name="userId">The user id.</param>
        /// <param name="userToken">The token the reminder is about.</param>
        /// <param name="interval">How long to leave between reminders.</param>
        /// <returns><c>true</c> when the caller should show it.</returns>
        public bool MarkReloginPrompted(Guid userId, string userToken, TimeSpan interval)
        {
            lock (SaveLock)
            {
                var config = Live().GetByGuid(userId);
                if (config == null || !string.Equals(config.UserToken, userToken, StringComparison.Ordinal))
                {
                    return false;
                }

                if (config.LastReloginPromptAt != default && DateTime.UtcNow - config.LastReloginPromptAt < interval)
                {
                    return false;
                }

                config.LastReloginPromptAt = DateTime.UtcNow;
                SimklPlugin.Instance?.SaveConfiguration();
                return true;
            }
        }

        /// <summary>
        /// Switches a user's rewatch flag off, when the stored token is still the one Simkl answered for.
        /// </summary>
        /// <remarks>
        /// Simkl answers pro_required when the account behind the token is no longer Pro or VIP, and later stops must
        /// then go out without the flag.
        /// </remarks>
        /// <param name="userId">The user id.</param>
        /// <param name="userToken">The token the answer belongs to.</param>
        /// <returns><c>true</c> when the stored settings were changed.</returns>
        public bool DisableRewatch(Guid userId, string userToken)
        {
            lock (SaveLock)
            {
                var config = Live().GetByGuid(userId);
                if (config == null || !config.AllowRewatch || !string.Equals(config.UserToken, userToken, StringComparison.Ordinal))
                {
                    return false;
                }

                config.AllowRewatch = false;
                SimklPlugin.Instance?.SaveConfiguration();
                return true;
            }
        }

        /// <summary>
        /// Clears a token Simkl rejected, wherever it is stored.
        /// </summary>
        /// <param name="userToken">User token.</param>
        public void DeleteUserToken(string userToken)
        {
            // "no token" is not a token value. Matching on it would clear every user who is already signed out and
            // save the configuration for nothing, which is what a call carrying the emptied token of a login that
            // ended mid request would otherwise do.
            if (string.IsNullOrEmpty(userToken))
            {
                return;
            }

            lock (SaveLock)
            {
                var cleared = false;
                foreach (var config in Live().UserConfigs)
                {
                    if (!string.Equals(config.UserToken, userToken, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    config.ClearTokens();
                    cleared = true;
                }

                if (cleared)
                {
                    SimklPlugin.Instance?.SaveConfiguration();
                }
            }
        }

        /// <summary>
        /// Logs one user out and saves the configuration.
        /// </summary>
        /// <param name="userId">The user id.</param>
        public void LogOut(Guid userId)
        {
            lock (SaveLock)
            {
                var config = Live().GetByGuid(userId);
                if (config == null)
                {
                    return;
                }

                config.ClearTokens();
                SimklPlugin.Instance?.SaveConfiguration();
            }
        }

        // the page can replace the whole configuration object, so a writer must change the one the plugin is using
        private PluginConfiguration Live()
        {
            return SimklPlugin.Instance?.Configuration ?? this;
        }
    }
}
