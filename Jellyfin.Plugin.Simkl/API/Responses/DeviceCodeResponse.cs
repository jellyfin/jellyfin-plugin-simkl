using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// What POST /oauth2/device answers: the RFC 8628 device authorization response.
    /// </summary>
    public class DeviceCodeResponse
    {
        /// <summary>
        /// Gets or sets the code the plugin polls with. It is a credential and never reaches the browser.
        /// </summary>
        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; set; }

        /// <summary>
        /// Gets or sets the code the user types on simkl.com, formatted XXXX-YYYY.
        /// </summary>
        [JsonPropertyName("user_code")]
        public string? UserCode { get; set; }

        /// <summary>
        /// Gets or sets the page the user opens.
        /// </summary>
        [JsonPropertyName("verification_uri")]
        public string? VerificationUri { get; set; }

        /// <summary>
        /// Gets or sets the same page with the code already in it.
        /// </summary>
        [JsonPropertyName("verification_uri_complete")]
        public string? VerificationUriComplete { get; set; }

        /// <summary>
        /// Gets or sets how many seconds the code is good for.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        /// <summary>
        /// Gets or sets the shortest polling interval, in seconds.
        /// </summary>
        [JsonPropertyName("interval")]
        public int? Interval { get; set; }
    }
}
