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
            Title = item.OriginalTitle;
            Year = item.ProductionYear;
            Ids = new SimklMovieIds(item.ProviderIds);
            WatchedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH\\:mm\\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets or sets the movie title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the year.
        /// </summary>
        public int? Year { get; set; }

        /// <inheritdoc />
        public override SimklIds Ids { get; set; }

        /// <summary>
        /// Gets watched at.
        /// </summary>
        public string WatchedAt { get; }
    }
}