using System;
using System.Globalization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl movie.
    /// </summary>
    public class SimklMovie : SimklMediaObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklMovie"/> class.
        /// </summary>
        /// <param name="item">The base item dto.</param>
        public SimklMovie(BaseItemDto item)
        {
            title = item.OriginalTitle;
            year = item.ProductionYear;
            ids = new SimklMovieIds(item.ProviderIds);
            watched_at = DateTime.UtcNow.ToString("yyyy-MM-dd HH\\:mm\\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets or sets the movie title.
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// Gets or sets the year.
        /// </summary>
        public int? year { get; set; }

        /// <inheritdoc />
        public override SimklIds ids { get; set; }

        /// <summary>
        /// Gets watched at.
        /// </summary>
        public string watched_at { get; }
    }
}