using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The answer of POST /sync/history (added) and /sync/history/remove (deleted); not_found echoes the entries
    /// Simkl could not identify.
    /// </summary>
    public class SyncHistoryResponse
    {
        /// <summary>
        /// Gets or sets what was added.
        /// </summary>
        [JsonPropertyName("added")]
        public SyncHistoryResponseCount? Added { get; set; }

        /// <summary>
        /// Gets or sets what was removed.
        /// </summary>
        [JsonPropertyName("deleted")]
        public SyncHistoryResponseCount? Deleted { get; set; }

        /// <summary>
        /// Gets or sets the entries Simkl does not know.
        /// </summary>
        [JsonPropertyName("not_found")]
        public SyncHistoryNotFound? NotFound { get; set; }
    }
}
