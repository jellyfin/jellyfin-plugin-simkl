using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The RFC 6749 error envelope every /oauth2/ route answers a refusal with.
    /// </summary>
    public class OAuthErrorResponse
    {
        /// <summary>
        /// The device flow is still waiting for the user to approve.
        /// </summary>
        public const string AuthorizationPending = "authorization_pending";

        /// <summary>
        /// The plugin polled inside the five second floor and must wait longer.
        /// </summary>
        public const string SlowDown = "slow_down";

        /// <summary>
        /// The device code ran out, or was never valid for this client.
        /// </summary>
        public const string ExpiredToken = "expired_token";

        /// <summary>
        /// The refresh token was rejected, which ends the login for good.
        /// </summary>
        public const string InvalidGrant = "invalid_grant";

        /// <summary>
        /// The user said no on simkl.com.
        /// </summary>
        public const string AccessDenied = "access_denied";

        /// <summary>
        /// The client id is unknown or not enabled for OAuth 2.
        /// </summary>
        public const string InvalidClient = "invalid_client";

        /// <summary>
        /// The request was malformed, which means the plugin sent it wrong.
        /// </summary>
        public const string InvalidRequest = "invalid_request";

        /// <summary>
        /// Simkl does not serve the grant that was asked for.
        /// </summary>
        public const string UnsupportedGrantType = "unsupported_grant_type";

        /// <summary>
        /// Gets or sets the machine readable error.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets the human readable reason.
        /// </summary>
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }

        /// <summary>
        /// Tells a refusal that ends a device login from a failure worth polling through.
        /// </summary>
        /// <remarks>
        /// Only the errors listed here are answers about the login itself. Anything else - a 502 from whatever sits
        /// in front of api.simkl.com, a rate limit, a connection reset - says nothing about the device code, which
        /// is good for fifteen minutes and may already have been approved. Treating those as fatal threw a live
        /// login away on a single blip.
        /// </remarks>
        /// <param name="error">The error Simkl answered with, or a synthesized one for a non-RFC answer.</param>
        /// <returns><c>true</c> when the login is over.</returns>
        public static bool IsTerminal(string? error)
        {
            return error switch
            {
                ExpiredToken or AccessDenied or InvalidGrant or InvalidClient or InvalidRequest or UnsupportedGrantType => true,
                _ => false
            };
        }
    }
}
