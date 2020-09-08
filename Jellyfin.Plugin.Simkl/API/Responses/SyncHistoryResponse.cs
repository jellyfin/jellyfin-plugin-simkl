using System.Text.Json.Serialization;
#pragma warning disable SA1300

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
        public SyncHistoryNotFound not_found { get; set; }
    }
}