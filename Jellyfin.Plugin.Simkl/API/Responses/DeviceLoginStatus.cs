using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// What the plugin page gets back from the two device login routes.
    /// </summary>
    /// <remarks>
    /// The device code itself is not in here. It stays on the server, in
    /// <see cref="Services.DeviceLoginStore"/>, and the completed login is written straight into the user's
    /// settings, so no Simkl credential ever reaches the browser.
    /// </remarks>
    public class DeviceLoginStatus
    {
        /// <summary>
        /// Waiting for the user to approve the code on simkl.com.
        /// </summary>
        public const string StatusPending = "pending";

        /// <summary>
        /// The user approved, and the login is stored.
        /// </summary>
        public const string StatusConnected = "connected";

        /// <summary>
        /// The code ran out, or nothing was started for this user.
        /// </summary>
        public const string StatusExpired = "expired";

        /// <summary>
        /// Simkl could not be reached, or refused for a reason the page cannot act on.
        /// </summary>
        public const string StatusError = "error";

        /// <summary>
        /// Gets or sets one of the status constants.
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = StatusPending;

        /// <summary>
        /// Gets or sets the code the user types, formatted XXXX-YYYY.
        /// </summary>
        [JsonPropertyName("userCode")]
        public string? UserCode { get; set; }

        /// <summary>
        /// Gets or sets the page the user opens.
        /// </summary>
        [JsonPropertyName("verificationUri")]
        public string? VerificationUri { get; set; }

        /// <summary>
        /// Gets or sets the same page with the code already in it.
        /// </summary>
        [JsonPropertyName("verificationUriComplete")]
        public string? VerificationUriComplete { get; set; }

        /// <summary>
        /// Gets or sets how many seconds are left before the code runs out.
        /// </summary>
        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Gets or sets how many seconds to wait before polling again.
        /// </summary>
        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        /// <summary>
        /// Gets or sets the reason, when there is one worth showing.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
