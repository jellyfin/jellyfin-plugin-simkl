using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The entries of a history call Simkl could not identify, echoed back as they were sent.
    /// </summary>
    public class SyncHistoryNotFound
    {
        /// <summary>
        /// Gets or sets the movies.
        /// </summary>
        [JsonPropertyName("movies")]
        public IReadOnlyList<HistoryMovie>? Movies { get; set; }

        /// <summary>
        /// Gets or sets the shows.
        /// </summary>
        [JsonPropertyName("shows")]
        public IReadOnlyList<HistoryShow>? Shows { get; set; }

        /// <summary>
        /// Gets or sets the episodes.
        /// </summary>
        [JsonPropertyName("episodes")]
        public IReadOnlyList<HistoryEpisode>? Episodes { get; set; }
    }
}
