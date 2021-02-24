using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl File.
    /// </summary>
    public class SimklFile
    {
        /// <summary>
        /// Gets or sets the file.
        /// </summary>
        [JsonPropertyName("file")]
        public string? File { get; set; }

        /// <summary>
        /// Gets or sets the part.
        /// </summary>
        [JsonPropertyName("part")]
        public int? Part { get; set; }

        /// <summary>
        /// Gets or sets the hash.
        /// </summary>
        [JsonPropertyName("hash")]
        public string? Hash { get; set; }
    }
}