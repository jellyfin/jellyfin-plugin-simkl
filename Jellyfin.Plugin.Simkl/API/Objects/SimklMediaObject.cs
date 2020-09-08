namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl media object.
    /// </summary>
    public abstract class SimklMediaObject
    {
        /// <summary>
        /// Gets or sets ids.
        /// </summary>
        public abstract SimklIds Ids { get; set; }
    }
}
