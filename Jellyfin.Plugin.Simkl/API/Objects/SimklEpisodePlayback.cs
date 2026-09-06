using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl episode playback.
    /// </summary>
    public class SimklEpisodePlayback : SimklMediaObject
    {
        /// <summary>
        /// Gets or sets the episode title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("season")]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number.
        /// </summary>
        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        /// <summary>
        /// Gets or sets progress in percentage.
        /// </summary>
        [JsonPropertyName("progress")]
        public int? Progress { get; set; }
    }
}
