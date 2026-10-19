#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// The body of POST /sync/history and /sync/history/remove: what a user marked, or unmarked, by hand.
    /// </summary>
    public class HistoryRequest
    {
        /// <summary>
        /// Gets or sets the movies.
        /// </summary>
        [JsonPropertyName("movies")]
        public List<HistoryMovie> Movies { get; set; } = new List<HistoryMovie>();

        /// <summary>
        /// Gets or sets the shows, each naming the episodes it covers.
        /// </summary>
        [JsonPropertyName("shows")]
        public List<HistoryShow> Shows { get; set; } = new List<HistoryShow>();

        /// <summary>
        /// Gets or sets the episodes found by their own TVDB id, with no show and no season mapping involved.
        /// </summary>
        [JsonPropertyName("episodes")]
        public List<HistoryEpisode> Episodes { get; set; } = new List<HistoryEpisode>();
    }
}
