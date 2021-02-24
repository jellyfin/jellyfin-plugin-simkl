using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// User settings.
    /// </summary>
    public class UserSettings
    {
        /// <summary>
        /// Gets or sets user.
        /// </summary>
        [JsonPropertyName("user")]
        public User? User { get; set; }

        /// <summary>
        /// Gets or sets error.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}