#pragma warning disable CA2227

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// A show in a history request; a show without seasons would cover the whole show.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from <see cref="SimklShow"/>. That one describes a show to the scrobble and sync
    /// endpoints, which carry their own season shape and a typed watched_at; a history request states the dates as
    /// the exact strings Simkl is to store them under.
    /// </remarks>
    public class HistoryShow
    {
        /// <summary>
        /// Gets or sets the show title, used when the ids identify nothing.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the first air year, which goes with the title.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the ids of the show.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklIds? Ids { get; set; }

        /// <summary>
        /// Gets or sets the seasons this request touches.
        /// </summary>
        [JsonPropertyName("seasons")]
        public List<HistorySeason> Seasons { get; set; } = new List<HistorySeason>();
    }
}
