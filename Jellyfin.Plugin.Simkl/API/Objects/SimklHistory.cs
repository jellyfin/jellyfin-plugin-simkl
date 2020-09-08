#pragma warning disable CA2227

using System.Collections.Generic;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl history container.
    /// </summary>
    public class SimklHistory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklHistory"/> class.
        /// </summary>
        public SimklHistory()
        {
            Movies = new List<SimklMovie>();
            Shows = new List<SimklShow>();
            Episodes = new List<SimklEpisode>();
        }

        /// <summary>
        /// Gets or sets list of movies.
        /// </summary>
        public List<SimklMovie> Movies { get; set; }

        /// <summary>
        /// Gets or sets the list of shows.
        /// </summary>
        public List<SimklShow> Shows { get; set; }

        /// <summary>
        /// Gets or sets the list of episodes.
        /// </summary>
        public List<SimklEpisode> Episodes { get; set; }
    }
}