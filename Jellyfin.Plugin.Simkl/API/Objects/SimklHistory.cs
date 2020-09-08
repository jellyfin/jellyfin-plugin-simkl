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
            movies = new List<SimklMovie>();
            shows = new List<SimklShow>();
            episodes = new List<SimklEpisode>();
        }

        /// <summary>
        /// Gets or sets list of movies.
        /// </summary>
        public List<SimklMovie> movies { get; set; }

        /// <summary>
        /// Gets or sets the list of shows.
        /// </summary>
        public List<SimklShow> shows { get; set; }

        /// <summary>
        /// Gets or sets the list of episodes.
        /// </summary>
        public List<SimklEpisode> episodes { get; set; }
    }
}