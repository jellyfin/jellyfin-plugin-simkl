using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The account part of the Simkl settings.
    /// </summary>
    public class UserSettingsAccount
    {
        /// <summary>
        /// Gets or sets the Simkl account id.
        /// </summary>
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        /// <summary>
        /// Gets or sets the plan: free, pro or vip.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}
