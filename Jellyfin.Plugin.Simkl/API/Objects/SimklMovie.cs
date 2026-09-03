using System;
using System.Text.Json.Serialization;
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
        public SimklMovie()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklMovie"/> class.
        /// </summary>
        /// <param name="item">The base item dto.</param>
        public SimklMovie(BaseItemDto item)
        {
            Title = item.OriginalTitle;
            Year = item.ProductionYear;
            Ids = new SimklMovieIds(item.ProviderIds);
            WatchedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets or sets the movie title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets watched at.
        /// </summary>
        [JsonPropertyName("watched_at")]
        public DateTime? WatchedAt { get; set; }
    }
}