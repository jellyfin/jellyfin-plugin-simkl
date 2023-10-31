using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl movie ids.
    /// </summary>
    public class SimklMovieIds : SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklMovieIds"/> class.
        /// </summary>
        /// <param name="providerMovieIds">the provider movie ids.</param>
        public SimklMovieIds(Dictionary<string, string> providerMovieIds)
            : base(providerMovieIds)
        {
        }

        /// <summary>
        /// Gets or sets the mal id.
        /// </summary>
        [JsonPropertyName("mal")]
        public int? Mal { get; set; }

        /// <summary>
        /// Gets or sets the hulu id.
        /// </summary>
        [JsonPropertyName("hulu")]
        public int? Hulu { get; set; }

        /// <summary>
        /// Gets or sets the crunchyroll id.
        /// </summary>
        [JsonPropertyName("crunchyroll")]
        public int? Crunchyroll { get; set; }

        /// <summary>
        /// Gets or sets the movie db id.
        /// </summary>
        [JsonPropertyName("moviedb")]
        public string? Moviedb { get; set; }
    }
}