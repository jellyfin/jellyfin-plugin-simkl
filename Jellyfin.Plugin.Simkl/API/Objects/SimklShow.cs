using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl show.
    /// </summary>
    public class SimklShow : SimklMediaObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklShow"/> class.
        /// </summary>
        /// <param name="mediaInfo">The media info.</param>
        public SimklShow(BaseItemDto mediaInfo)
        {
            Title = mediaInfo.SeriesName;
            Ids = new SimklShowIds(mediaInfo.ProviderIds);
            Year = mediaInfo.ProductionYear;
            Seasons = new[]
            {
                new Season
                {
                    Number = mediaInfo.ParentIndexNumber,
                    Episodes = new[]
                    {
                        new ShowEpisode
                        {
                            Number = mediaInfo.IndexNumber
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Gets or sets title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets seasons.
        /// </summary>
        [JsonPropertyName("seasons")]
        public IReadOnlyList<Season> Seasons { get; set; }
    }
}