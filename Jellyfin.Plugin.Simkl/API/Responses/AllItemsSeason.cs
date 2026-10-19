using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// One watched season of an all-items entry.
    /// </summary>
    public class AllItemsSeason
    {
        /// <summary>
        /// Gets or sets the season number, in Simkl's own numbering.
        /// </summary>
        [JsonPropertyName("number")]
        public int Number { get; set; }

        /// <summary>
        /// Gets or sets the watched episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public IReadOnlyList<AllItemsEpisode>? Episodes { get; set; }
    }
}
