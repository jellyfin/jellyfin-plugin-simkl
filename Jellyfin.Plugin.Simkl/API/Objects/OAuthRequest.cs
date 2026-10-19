using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// The body of a call to /oauth2/device or /oauth2/token. One shape for all of them: Simkl reads the keys the
    /// grant at hand needs and the serializer drops the nulls, so a device init carries no grant_type and a
    /// renewal carries no scope.
    /// </summary>
    /// <remarks>
    /// The client id travels in the body on purpose. The API takes it from the body first, then the query string,
    /// then the simkl-api-key header, and the body is the one place none of the plugin's own request decoration
    /// can reach.
    /// </remarks>
    public class OAuthRequest
    {
        /// <summary>
        /// The grant that exchanges an approved device code for tokens.
        /// </summary>
        public const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";

        /// <summary>
        /// The grant that renews an access token.
        /// </summary>
        public const string RefreshTokenGrant = "refresh_token";

        /// <summary>
        /// Gets or sets the client id of the app the tokens belong to.
        /// </summary>
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        /// <summary>
        /// Gets or sets the grant type. Not sent when starting a device flow.
        /// </summary>
        [JsonPropertyName("grant_type")]
        public string? GrantType { get; set; }

        /// <summary>
        /// Gets or sets the scope being asked for. Only sent when starting a device flow: a renewal that names one
        /// can only narrow the grant, and Simkl refuses a widening.
        /// </summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        /// <summary>
        /// Gets or sets the device code being polled.
        /// </summary>
        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; set; }

        /// <summary>
        /// Gets or sets the refresh token being redeemed.
        /// </summary>
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }
}
