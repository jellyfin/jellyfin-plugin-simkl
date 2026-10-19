using System;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    /// <summary>
    /// Per user plugin settings.
    /// </summary>
    public class UserConfig
    {
        /// <summary>
        /// The automatic import interval a user starts with, in hours.
        /// </summary>
        public const int DefaultAutoImportHours = 24;

        /// <summary>
        /// The prefix every OAuth 2 access token carries. A token without it was issued by the old pin flow and
        /// belongs to the other client id, which is the whole reason the two are told apart.
        /// </summary>
        public const string OAuth2Prefix = "simkl_at_";

        // an access token lives seven days; renewing it a day early leaves room for a server that was off
        private static readonly TimeSpan RenewBefore = TimeSpan.FromHours(24);

        /// <summary>
        /// Initializes a new instance of the <see cref="UserConfig"/> class.
        /// </summary>
        public UserConfig()
        {
            ScrobbleMovies = true;
            ScrobbleShows = true;
            SyncWatchedMarks = true;
            MinLength = 5;
            AutoImportHours = DefaultAutoImportHours;
            UserToken = string.Empty;
            RefreshToken = string.Empty;
            TokenScope = string.Empty;
        }

        /// <summary>
        /// Gets or sets the Jellyfin user id these settings belong to.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether movies are reported to Simkl.
        /// </summary>
        public bool ScrobbleMovies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether episodes are reported to Simkl.
        /// </summary>
        public bool ScrobbleShows { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether marking something played or unplayed in Jellyfin by hand is
        /// mirrored to the Simkl history. On by default, and independent of the two scrobbling switches: playback
        /// keeps being reported even when this is off.
        /// </summary>
        /// <remarks>
        /// A user with a Simkl history they curate elsewhere can switch this off so their own tidying in Jellyfin -
        /// marking a season played, clearing a show - never reaches Simkl. It has no effect on the import, which
        /// only ever writes into Jellyfin.
        /// </remarks>
        public bool SyncWatchedMarks { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a stop is sent with allow_rewatch, so a title Simkl already has in
        /// the history is logged as a rewatch instead of being ignored. Simkl Pro/VIP only.
        /// </summary>
        public bool AllowRewatch { get; set; }

        /// <summary>
        /// Gets or sets the minimum length, in minutes, an item must have to be reported.
        /// </summary>
        public int MinLength { get; set; }

        /// <summary>
        /// Gets or sets the Simkl access token. Empty when the user is logged out.
        /// </summary>
        public string UserToken { get; set; }

        /// <summary>
        /// Gets or sets the OAuth 2 refresh token. Empty for a login made with the old pin flow, which issued a token
        /// that never expires and has nothing to refresh.
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// Gets or sets when the access token expires, in UTC. Default for a token from the old pin flow.
        /// </summary>
        public DateTime TokenExpiresAt { get; set; }

        /// <summary>
        /// Gets or sets the scope Simkl granted, as the space separated string it answered with.
        /// </summary>
        public string TokenScope { get; set; }

        /// <summary>
        /// Gets or sets when this user was last asked to sign in again, in UTC. Only the old pin flow tokens are
        /// ever asked.
        /// </summary>
        public DateTime LastReloginPromptAt { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the scheduled task imports this user's Simkl watch history.
        /// The import buttons on the plugin page work regardless.
        /// </summary>
        public bool AutoImport { get; set; }

        /// <summary>
        /// Gets or sets how often, in hours, the hourly task checks this user's Simkl activity.
        /// </summary>
        public int AutoImportHours { get; set; }

        /// <summary>
        /// Gets a value indicating whether the stored token came from the OAuth 2 device flow. Told apart by the
        /// prefix, the same way the API itself does it.
        /// </summary>
        [XmlIgnore]
        public bool IsOAuth2 => IsOAuth2Token(UserToken);

        /// <summary>
        /// Gets the value that stands for this login for as long as it lasts, for anything that has to notice the
        /// user switching Simkl accounts.
        /// </summary>
        /// <remarks>
        /// Not the access token. An OAuth 2 one is renewed every few days, and anything keyed on it would read
        /// every renewal as a different account: the import would throw its cursors away and pull the whole
        /// history again, once a week, forever. The refresh token is what lasts as long as the grant does, and a
        /// fresh sign in mints a new one, which is exactly when starting over is right. A pin flow login has only
        /// the access token, which never changes either.
        /// </remarks>
        [XmlIgnore]
        public string AccountAnchor => IsOAuth2 && !string.IsNullOrEmpty(RefreshToken) ? RefreshToken : UserToken;

        /// <summary>
        /// Gets a value indicating whether the access token is close enough to its expiry to be renewed.
        /// </summary>
        [XmlIgnore]
        public bool NeedsRefresh => IsOAuth2
            && !string.IsNullOrEmpty(RefreshToken)
            && (TokenExpiresAt == default || TokenExpiresAt - DateTime.UtcNow < RenewBefore);

        /// <summary>
        /// Tells a device flow access token from one the old pin flow issued.
        /// </summary>
        /// <param name="token">The token to look at.</param>
        /// <returns><c>true</c> for an OAuth 2 access token.</returns>
        public static bool IsOAuth2Token(string? token)
        {
            return token != null && token.StartsWith(OAuth2Prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Takes the whole login over from another copy of these settings, or clears it when there is none.
        /// </summary>
        /// <remarks>
        /// The five token fields are server owned and always move together: a refresh token left behind by a token
        /// it does not belong to would renew a login into the wrong account's tokens.
        /// </remarks>
        /// <param name="source">The settings to copy the login from.</param>
        public void CopyTokensFrom(UserConfig? source)
        {
            UserToken = source?.UserToken ?? string.Empty;
            RefreshToken = source?.RefreshToken ?? string.Empty;
            TokenExpiresAt = source?.TokenExpiresAt ?? default;
            TokenScope = source?.TokenScope ?? string.Empty;
            LastReloginPromptAt = source?.LastReloginPromptAt ?? default;
        }

        /// <summary>
        /// Clears the whole login.
        /// </summary>
        public void ClearTokens()
        {
            CopyTokensFrom(null);
        }
    }
}
