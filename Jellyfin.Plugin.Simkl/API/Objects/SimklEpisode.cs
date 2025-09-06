using System;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl episode container.
    /// </summary>
    public class SimklEpisode : SimklMediaObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklEpisode"/> class.
        /// </summary>
        public SimklEpisode()
        {
            // Parameterless constructor required for deserialization.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklEpisode"/> class.
        /// </summary>
        /// <param name="media">Episode Data.</param>
        public SimklEpisode(BaseItemDto media)
        {
            Title = media?.SeriesName;
            Ids = media is not null ? new SimklIds(media.ProviderIds) : null;
            Year = media?.ProductionYear;
            Season = media?.ParentIndexNumber;
            Episode = media?.IndexNumber;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklEpisode"/> class.
        /// </summary>
        /// <param name="media">Episode Data from the library.</param>
        public SimklEpisode(BaseItem media)
        {
            if (media is Episode episode)
            {
                Title = episode.Series?.Name;
                Ids = new SimklIds(episode.ProviderIds);
                Year = episode.ProductionYear;
                Season = episode.ParentIndexNumber;
                Episode = episode.IndexNumber;
            }
        }

        /// <summary>
        /// Gets or sets watched at.
        /// </summary>
        [JsonPropertyName("watched_at")]
        public DateTime? WatchedAt { get; set; }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the season.
        /// </summary>
        [JsonPropertyName("season")]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode.
        /// </summary>
        [JsonPropertyName("episode")]
        public int? Episode { get; set; }

        /// <summary>
        /// Gets or sets multipart.
        /// </summary>
        [JsonPropertyName("multipart")]
        public bool? Multipart { get; set; }
    }
}