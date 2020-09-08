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
        public string file { get; set; }

        /// <summary>
        /// Gets or sets the part.
        /// </summary>
        public int? part { get; set; }

        /// <summary>
        /// Gets or sets the hash.
        /// </summary>
        public string hash { get; set; }
    }
}