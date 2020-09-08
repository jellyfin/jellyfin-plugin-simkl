namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl File.
    /// </summary>
    public class SimklFile
    {
        /// <summary>
        /// Gets or sets the file.
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// Gets or sets the part.
        /// </summary>
        public int? Part { get; set; }

        /// <summary>
        /// Gets or sets the hash.
        /// </summary>
        public string Hash { get; set; }
    }
}