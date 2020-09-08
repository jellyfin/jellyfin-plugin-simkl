namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Season.
    /// </summary>
    public class Season
    {
        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        public int? number { get; set; }

        /// <summary>
        /// Gets or sets the episodes.
        /// </summary>
        public ShowEpisode[] episodes { get; set; }
    }
}