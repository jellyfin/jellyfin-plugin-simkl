using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// How many entries of each kind a history call touched.
    /// </summary>
    public class SyncHistoryResponseCount
    {
        /// <summary>
        /// Gets or sets the movie count.
        /// </summary>
        [JsonPropertyName("movies")]
        public int Movies { get; set; }

        /// <summary>
        /// Gets or sets the show count.
        /// </summary>
        [JsonPropertyName("shows")]
        public int Shows { get; set; }

        /// <summary>
        /// Gets or sets the episode count.
        /// </summary>
        [JsonPropertyName("episodes")]
        public int Episodes { get; set; }
    }
}
