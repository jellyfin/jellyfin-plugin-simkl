using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// One watched episode of an all-items entry.
    /// </summary>
    public class AllItemsEpisode
    {
        /// <summary>
        /// Gets or sets the episode number, in Simkl's own numbering.
        /// </summary>
        [JsonPropertyName("number")]
        public int Number { get; set; }

        /// <summary>
        /// Gets or sets when this episode was watched.
        /// </summary>
        [JsonPropertyName("watched_at")]
        public string? WatchedAt { get; set; }

        /// <summary>
        /// Gets or sets the TVDB coordinates of this episode.
        /// </summary>
        [JsonPropertyName("tvdb")]
        public AllItemsTvdbCoords? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets the ids of this episode.
        /// </summary>
        [JsonPropertyName("ids")]
        public AllItemsEpisodeIds? Ids { get; set; }
    }
}
