using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// One episode of a history request.
    /// </summary>
    public class HistoryEpisode
    {
        /// <summary>
        /// Gets or sets the episode number, null on the top level route where only the ids place it.
        /// </summary>
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        /// <summary>
        /// Gets or sets when it was watched, as "yyyy-MM-ddTHH:mm:ssZ".
        /// </summary>
        [JsonPropertyName("watched_at")]
        public string? WatchedAt { get; set; }

        /// <summary>
        /// Gets or sets the ids of the episode itself.
        /// </summary>
        [JsonPropertyName("ids")]
        public HistoryEpisodeIds? Ids { get; set; }
    }
}
