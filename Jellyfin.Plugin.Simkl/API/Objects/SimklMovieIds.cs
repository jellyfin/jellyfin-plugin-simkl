using System.Collections.Generic;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl movie ids.
    /// </summary>
    public class SimklMovieIds : SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklMovieIds"/> class.
        /// </summary>
        /// <param name="providerMovieIds">the provider movie ids.</param>
        public SimklMovieIds(Dictionary<string, string> providerMovieIds)
            : base(providerMovieIds)
        {
        }

        /// <summary>
        /// Gets or sets the tvdb id.
        /// </summary>
        public int? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets the mal id.
        /// </summary>
        public int? Mal { get; set; }

        /// <summary>
        /// Gets or sets the anidb id.
        /// </summary>
        public int? Anidb { get; set; }

        /// <summary>
        /// Gets or sets the hulu id.
        /// </summary>
        public int? Hulu { get; set; }

        /// <summary>
        /// Gets or sets the crunchyroll id.
        /// </summary>
        public int? Crunchyroll { get; set; }

        /// <summary>
        /// Gets or sets the movie db id.
        /// </summary>
        public string Moviedb { get; set; }
    }
}