using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Simkl
{
    /// <summary>
    /// SIMKL tracker.
    /// </summary>
    public class SimklPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklPlugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public SimklPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the current instance of the plugin.
        /// </summary>
        public static SimklPlugin? Instance { get; private set; }

        /// <inheritdoc />
        public override Guid Id => new Guid("07CAEF58-A94B-4211-A62C-F9774E04EBDB");

        /// <inheritdoc />
        public override string Name => "Simkl";

        /// <inheritdoc />
        public override string Description => "Scrobble your watched Movies, TV Shows and Anime to Simkl and share your progress with friends!";

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            // Admin configuration page only
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            };
        }

        /// <inheritdoc />
        /// <remarks>
        /// The dashboard posts the whole configuration, so a snapshot taken before the last login would log users
        /// out. The stored token is carried over first. Import cursors are not involved: they live in their own file,
        /// see <see cref="Services.ImportStateStore"/>.
        /// </remarks>
        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            if (configuration is PluginConfiguration incoming)
            {
                // carried over and installed under one lock, so a login or a logout landing mid save is not
                // written to the configuration that is about to be replaced
                PluginConfiguration.ReplaceRunning(() => Configuration, incoming, () => base.UpdateConfiguration(incoming));
                return;
            }

            base.UpdateConfiguration(configuration);
        }
    }
}
