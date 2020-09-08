using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// sync history not found.
    /// </summary>
    public class SyncHistoryNotFound
    {
        /// <summary>
        /// Gets or sets movies.
        /// </summary>
        public SimklMovie[] Movies { get; set; }

        /// <summary>
        /// Gets or sets shows.
        /// </summary>
        public SimklShow[] Shows { get; set; }

        /// <summary>
        /// Gets or sets episodes.
        /// </summary>
        public SimklEpisode[] Episodes { get; set; }
    }
}