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
        public User user { get; set; }

        /// <summary>
        /// Gets or sets error.
        /// </summary>
        public string error { get; set; }
    }
}