namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Sync history response count.
    /// </summary>
    public class SyncHistoryResponseCount
    {
        /// <summary>
        /// Gets or sets movies.
        /// </summary>
        public int Movies { get; set; }

        /// <summary>
        /// Gets or sets shows.
        /// </summary>
        public int Shows { get; set; }

        /// <summary>
        /// Gets or sets episodes.
        /// </summary>
        public int Episodes { get; set; }
    }
}