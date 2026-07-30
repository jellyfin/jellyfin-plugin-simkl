using System.Collections.Generic;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl Episode Ids.
    /// </summary>
    public class SimklEpisodeIds : SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklEpisodeIds"/> class.
        /// </summary>
        /// <param name="providerIds">The provider ids.</param>
        public SimklEpisodeIds(IReadOnlyDictionary<string, string> providerIds) : base(providerIds)
        {
        }
    }
}