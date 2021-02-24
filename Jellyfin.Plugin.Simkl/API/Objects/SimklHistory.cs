#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl history container.
    /// </summary>
    public class SimklHistory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklHistory"/> class.
        /// </summary>
        public SimklHistory()
        {
            Movies = new List<SimklMovie>();
            Shows = new List<SimklShow>();
            Episodes = new List<SimklEpisode>();
        }

        /// <summary>
        /// Gets or sets list of movies.
        /// </summary>
        [JsonPropertyName("movies")]
        public List<SimklMovie> Movies { get; set; }

        /// <summary>
        /// Gets or sets the list of shows.
        /// </summary>
        [JsonPropertyName("shows")]
        public List<SimklShow> Shows { get; set; }

        /// <summary>
        /// Gets or sets the list of episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public List<SimklEpisode> Episodes { get; set; }
    }
}