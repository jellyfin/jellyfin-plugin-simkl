using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Sync history response.
    /// </summary>
    public class SyncHistoryResponse
    {
        /// <summary>
        /// Gets or sets added.
        /// </summary>
        public SyncHistoryResponseCount Added { get; set; }

        /// <summary>
        /// Gets or sets not found.
        /// </summary>
        [JsonPropertyName("not_found")]
        public SyncHistoryNotFound NotFound { get; set; }
    }
}