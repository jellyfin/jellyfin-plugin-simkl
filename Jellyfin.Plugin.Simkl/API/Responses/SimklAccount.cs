using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// What the plugin page gets back from GET /Simkl/account/{userId}. The property names are the ones the page
    /// reads, so they are spelled out: the Jellyfin API serializes its own responses in PascalCase.
    /// </summary>
    public class SimklAccount
    {
        /// <summary>
        /// Gets or sets the Simkl account id; null when the user is not logged in or the token was rejected.
        /// </summary>
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        /// <summary>
        /// Gets or sets the Simkl user name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the plan: free, pro or vip. The page shows the plan and asks free accounts for support.
        /// </summary>
        [JsonPropertyName("plan")]
        public string? Plan { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this login came from the old pin flow. It keeps working, but the
        /// page asks for it to be redone so the account moves to the OAuth 2 app.
        /// </summary>
        [JsonPropertyName("legacy")]
        public bool Legacy { get; set; }
    }
}
