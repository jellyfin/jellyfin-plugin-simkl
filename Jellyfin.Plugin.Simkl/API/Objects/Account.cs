namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Account.
    /// </summary>
    public class Account
    {
        /// <summary>
        /// Gets or sets account id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets timezone.
        /// </summary>
        public string Timezone { get; set; }
    }
}