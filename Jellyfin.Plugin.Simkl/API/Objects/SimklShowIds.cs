using System.Collections.Generic;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl show ids.
    /// </summary>
    public class SimklShowIds : SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklShowIds"/> class.
        /// </summary>
        /// <param name="providerMovieIds">The provider movie ids.</param>
        public SimklShowIds(Dictionary<string, string> providerMovieIds)
            : base(providerMovieIds)
        {
        }

        /// <summary>
        /// Gets or sets tvdb.
        /// </summary>
        public int? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets mal.
        /// </summary>
        public int? Mal { get; set; }

        /// <summary>
        /// Gets or sets anidb.
        /// </summary>
        public int? Anidb { get; set; }

        /// <summary>
        /// Gets or sets hulu.
        /// </summary>
        public int? Hulu { get; set; }

        /// <summary>
        /// Gets or sets crunchyroll.
        /// </summary>
        public int? Crunchyroll { get; set; }

        /// <summary>
        /// Gets or sets zap2it.
        /// </summary>
        public string Zap2It { get; set; }
    }
}