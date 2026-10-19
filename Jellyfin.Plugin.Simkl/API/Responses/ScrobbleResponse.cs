using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The answer of POST /scrobble/{action}.
    /// </summary>
    public class ScrobbleResponse
    {
        /// <summary>
        /// Gets or sets the id of the playback session Simkl keeps.
        /// </summary>
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        /// <summary>
        /// Gets or sets the action Simkl recorded.
        /// </summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the progress Simkl stored.
        /// </summary>
        [JsonPropertyName("progress")]
        public double? Progress { get; set; }

        /// <summary>
        /// Gets or sets the rewatch id; only with allow_rewatch=yes on a stop that marked something watched.
        /// </summary>
        [JsonPropertyName("rewatch_id")]
        public long? RewatchId { get; set; }

        /// <summary>
        /// Gets or sets the rewatch status: active, completed, closed, first_watch, too_soon, not_eligible or pro_required.
        /// </summary>
        [JsonPropertyName("rewatch_status")]
        public string? RewatchStatus { get; set; }

        /// <summary>
        /// Gets or sets the reason Simkl refused the call, which it also reports on an HTTP 200.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
