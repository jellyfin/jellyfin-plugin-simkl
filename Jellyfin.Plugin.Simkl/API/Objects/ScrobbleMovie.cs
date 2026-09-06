using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Movie sub-object for scrobble requests.
    /// </summary>
    public class ScrobbleMovie
    {
        /// <summary>
        /// Gets or sets the movie title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the production year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the movie ids.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklMovieIds? Ids { get; set; }
    }
}
