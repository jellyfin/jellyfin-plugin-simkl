using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The answer of GET /sync/all-items; an empty library answers {} or the literal null, so every list may be null.
    /// </summary>
    public class AllItemsResponse
    {
        /// <summary>
        /// Gets or sets the tv show entries.
        /// </summary>
        [JsonPropertyName("shows")]
        public IReadOnlyList<AllItemsEntry>? Shows { get; set; }

        /// <summary>
        /// Gets or sets the movie entries.
        /// </summary>
        [JsonPropertyName("movies")]
        public IReadOnlyList<AllItemsEntry>? Movies { get; set; }

        /// <summary>
        /// Gets or sets the anime entries.
        /// </summary>
        [JsonPropertyName("anime")]
        public IReadOnlyList<AllItemsEntry>? Anime { get; set; }
    }
}
