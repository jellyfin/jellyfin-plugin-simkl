using System.Text.Json.Serialization;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Code response.
    /// </summary>
    public class CodeResponse
    {
        /// <summary>
        /// Gets or sets result.
        /// </summary>
        public string? Result { get; set; }

        /// <summary>
        /// Gets or sets device code.
        /// </summary>
        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; set; }

        /// <summary>
        /// Gets or sets user code.
        /// </summary>
        [JsonPropertyName("user_code")]
        public string? UserCode { get; set; }

        /// <summary>
        /// Gets or sets verification url.
        /// </summary>
        [JsonPropertyName("verification_url")]
        public string? VerificationUrl { get; set; }

        /// <summary>
        /// Gets or sets expires in.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        /// <summary>
        /// Gets or sets interval.
        /// </summary>
        public int? Interval { get; set; }
    }
}