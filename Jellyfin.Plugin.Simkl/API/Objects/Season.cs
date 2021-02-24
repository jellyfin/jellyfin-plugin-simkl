using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Season.
    /// </summary>
    public class Season
    {
        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        /// <summary>
        /// Gets or sets the episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public IReadOnlyList<ShowEpisode> Episodes { get; set; } = Array.Empty<ShowEpisode>();
    }
}