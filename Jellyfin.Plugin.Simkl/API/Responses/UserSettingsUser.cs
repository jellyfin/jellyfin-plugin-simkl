using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The user part of the Simkl settings.
    /// </summary>
    public class UserSettingsUser
    {
        /// <summary>
        /// Gets or sets the Simkl user name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
