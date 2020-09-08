using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl Ids.
    /// </summary>
    public class SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklIds"/> class.
        /// </summary>
        /// <param name="providerIds">The provider ids.</param>
        public SimklIds(Dictionary<string, string> providerIds)
        {
            foreach (var (key, value) in providerIds)
            {
                if (key.Equals(nameof(simkl), StringComparison.OrdinalIgnoreCase))
                {
                    simkl = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                else if (key.Equals(nameof(imdb), StringComparison.OrdinalIgnoreCase))
                {
                    imdb = value;
                }
                else if (key.Equals(nameof(slug), StringComparison.OrdinalIgnoreCase))
                {
                    slug = value;
                }
                else if (key.Equals(nameof(netflix), StringComparison.OrdinalIgnoreCase))
                {
                    netflix = value;
                }
                else if (key.Equals(nameof(tmdb), StringComparison.OrdinalIgnoreCase))
                {
                    tmdb = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets simkl.
        /// </summary>
        public int? simkl { get; set; }

        /// <summary>
        /// Gets or sets the imdb id.
        /// </summary>
        public string imdb { get; set; }

        /// <summary>
        /// Gets or sets the slug.
        /// </summary>
        public string slug { get; set; }

        /// <summary>
        /// Gets or sets the netflix id.
        /// </summary>
        public string netflix { get; set; }

        /// <summary>
        /// Gets or sets the TMDb id.
        /// </summary>
        public string tmdb { get; set; }
    }
}