#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// One season of a show in a history request.
    /// </summary>
    public class HistorySeason
    {
        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("number")]
        public int Number { get; set; }

        /// <summary>
        /// Gets or sets the episodes of this season.
        /// </summary>
        [JsonPropertyName("episodes")]
        public List<HistoryEpisode> Episodes { get; set; } = new List<HistoryEpisode>();
    }
}
