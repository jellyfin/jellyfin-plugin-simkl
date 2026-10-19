using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The answer of POST /users/settings; only the fields the plugin shows are declared.
    /// </summary>
    public class UserSettingsResponse
    {
        /// <summary>
        /// Gets or sets the Simkl user.
        /// </summary>
        [JsonPropertyName("user")]
        public UserSettingsUser? User { get; set; }

        /// <summary>
        /// Gets or sets the Simkl account.
        /// </summary>
        [JsonPropertyName("account")]
        public UserSettingsAccount? Account { get; set; }
    }
}
