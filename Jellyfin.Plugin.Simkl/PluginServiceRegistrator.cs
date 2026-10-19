using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Simkl
{
    /// <inheritdoc />
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        /// <remarks>
        /// The scheduled task is deliberately not registered here: Jellyfin discovers <c>IScheduledTask</c> in the
        /// plugin assembly itself and builds it from the root provider.
        /// </remarks>
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<SimklApi>();

            // one lock over the per-user import bookkeeping, since storing it rewrites the whole plugin configuration
            serviceCollection.AddSingleton<ImportStateStore>();
            serviceCollection.AddSingleton<HistoryImporter>();

            // the device codes of logins in progress; a singleton because the page starts a login on one request
            // and polls it on the next
            serviceCollection.AddSingleton<DeviceLoginStore>();
            serviceCollection.AddSingleton<ReloginNudge>();

            serviceCollection.AddHostedService<PlaybackScrobbler>();
            serviceCollection.AddHostedService<WatchedMarks>();
        }
    }
}
