using System.Collections.Generic;
using System.Text.Json.Serialization;

#pragma warning disable CA2227

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl playback container for now watching.
    /// </summary>
    public class SimklPlayback
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklPlayback"/> class.
        /// </summary>
        public SimklPlayback()
        {
            Movies = new List<SimklMoviePlayback>();
            Shows = new List<SimklShowPlayback>();
            Episodes = new List<SimklEpisodePlayback>();
        }

        /// <summary>
        /// Gets or sets list of movies.
        /// </summary>
        [JsonPropertyName("movies")]
        public List<SimklMoviePlayback> Movies { get; set; }

        /// <summary>
        /// Gets or sets the list of shows.
        /// </summary>
        [JsonPropertyName("shows")]
        public List<SimklShowPlayback> Shows { get; set; }

        /// <summary>
        /// Gets or sets the list of episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public List<SimklEpisodePlayback> Episodes { get; set; }
    }
}
