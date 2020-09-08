using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Search file response.
    /// </summary>
    public class SearchFileResponse
    {
        /// <summary>
        /// Gets or sets type.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets episode.
        /// </summary>
        public SimklEpisode Episode { get; set; }

        /// <summary>
        /// Gets or sets movie.
        /// </summary>
        public SimklMovie Movie { get; set; }

        /// <summary>
        /// Gets or sets show.
        /// </summary>
        public SimklShow Show { get; set; }
    }
}