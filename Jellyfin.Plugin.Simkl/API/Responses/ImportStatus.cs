using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// What every /Simkl/import route answers. The property names are the ones the page reads, so they are
    /// spelled out: the Jellyfin API serializes its own responses in PascalCase.
    /// </summary>
    public class ImportStatus
    {
        /// <summary>
        /// Gets or sets a value indicating whether an import is running for this user right now.
        /// </summary>
        [JsonPropertyName("running")]
        public bool Running { get; set; }

        /// <summary>
        /// Gets or sets the current phase while running, and the final summary once done.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>
        /// Gets or sets why the run failed or was refused; null when fine.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets when the last import finished, as ISO UTC, or null.
        /// </summary>
        [JsonPropertyName("lastImportAt")]
        public string? LastImportAt { get; set; }

        /// <summary>
        /// Gets or sets when the scheduled task last checked this user, as ISO UTC, or null.
        /// </summary>
        [JsonPropertyName("lastAutoRunAt")]
        public string? LastAutoRunAt { get; set; }
    }
}
