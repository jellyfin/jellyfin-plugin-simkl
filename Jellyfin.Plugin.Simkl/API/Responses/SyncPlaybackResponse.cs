using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Response from /scrobble/start, /scrobble/pause, and /scrobble/stop.
    /// </summary>
    public class SyncPlaybackResponse
    {
        /// <summary>
        /// Gets or sets the scrobble session id.
        /// /scrobble/start always returns 0; pause and stop return a 64-bit id.
        /// </summary>
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        /// <summary>
        /// Gets or sets the action taken by the server.
        /// One of "start", "pause", or "scrobble" (watched).
        /// </summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the progress echoed back by the server (0–100).
        /// </summary>
        [JsonPropertyName("progress")]
        public float? Progress { get; set; }

        /// <summary>
        /// Gets or sets an error message, if any.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
