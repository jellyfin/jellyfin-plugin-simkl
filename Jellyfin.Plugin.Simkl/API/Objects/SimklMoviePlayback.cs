using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl movie playback.
    /// </summary>
    public class SimklMoviePlayback : SimklMediaObject
    {
        /// <summary>
        /// Gets or sets the movie title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets progress in percentage.
        /// </summary>
        [JsonPropertyName("progress")]
        public int? Progress { get; set; }
    }
}
