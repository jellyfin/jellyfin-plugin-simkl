using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The ids of an all-items episode.
    /// </summary>
    public class AllItemsEpisodeIds
    {
        /// <summary>
        /// Gets or sets the TVDB episode id.
        /// </summary>
        [JsonPropertyName("tvdb_id")]
        public int? TvdbId { get; set; }
    }
}
