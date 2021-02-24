using System;
using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configure it.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            UserConfigs = Array.Empty<UserConfig>();
        }

        /// <summary>
        /// Gets or sets the list of user configs.
        /// </summary>
        public UserConfig[] UserConfigs { get; set; }

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
        /// Delete user token.
        /// </summary>
        /// <param name="userToken">User token.</param>
        public void DeleteUserToken(string userToken)
        {
            foreach (var config in UserConfigs)
            {
                if (config.UserToken == userToken)
                {
                    config.UserToken = string.Empty;
                }
            }

            SimklPlugin.Instance?.SaveConfiguration();
        }
    }
}