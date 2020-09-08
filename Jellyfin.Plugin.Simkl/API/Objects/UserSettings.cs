namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// User settings.
    /// </summary>
    public class UserSettings
    {
        /// <summary>
        /// Gets or sets user.
        /// </summary>
        public User User { get; set; }

        /// <summary>
        /// Gets or sets account.
        /// </summary>
        public Account Account { get; set; }

        /// <summary>
        /// Gets or sets error.
        /// </summary>
        public string Error { get; set; }
    }
}