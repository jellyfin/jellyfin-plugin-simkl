namespace Jellyfin.Plugin.Simkl.Configuration
{
    /// <summary>
    /// Result and cursors of the Simkl history import. Only the history importer writes it, through
    /// <see cref="Services.ImportStateStore"/>, which keeps it in its own file rather than in the plugin configuration.
    /// </summary>
    public class ImportState
    {
        /// <summary>
        /// Gets or sets when the last run finished, as "yyyy-MM-ddTHH:mm:ssZ" UTC, or an empty string.
        /// </summary>
        public string LastImportAt { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human readable result of the last run, or an empty string.
        /// </summary>
        public string LastImportSummary { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets when a full import last completed, or an empty string. It decides full versus incremental.
        /// </summary>
        public string FullImportAt { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the activities "all" timestamp read by the last successful run, on Simkl's clock, or an
        /// empty string. It is the date_from of the next incremental call.
        /// </summary>
        public string SyncedAt { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets when the scheduled task, or switching the toggle on, last checked this user, or an empty
        /// string. It gates the per-user interval.
        /// </summary>
        public string LastAutoRunAt { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the newest shows watch activity covered by an import, or an empty string.
        /// </summary>
        public string CursorShows { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the newest anime watch activity covered by an import, or an empty string.
        /// </summary>
        public string CursorAnime { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the newest movies watch activity covered by an import, or an empty string.
        /// </summary>
        public string CursorMovies { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the hash of the Simkl token the cursors were taken under. A new login starts over.
        /// </summary>
        public string CursorAccount { get; set; } = string.Empty;

        /// <summary>
        /// Copies the state, so a running import and the stored configuration never share one object.
        /// </summary>
        /// <returns>An independent copy.</returns>
        public ImportState Clone()
        {
            return new ImportState
            {
                LastImportAt = LastImportAt,
                LastImportSummary = LastImportSummary,
                FullImportAt = FullImportAt,
                SyncedAt = SyncedAt,
                LastAutoRunAt = LastAutoRunAt,
                CursorShows = CursorShows,
                CursorAnime = CursorAnime,
                CursorMovies = CursorMovies,
                CursorAccount = CursorAccount
            };
        }
    }
}
