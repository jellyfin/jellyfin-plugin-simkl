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
            title = mediaInfo.SeriesName;
            ids = new SimklShowIds(mediaInfo.ProviderIds);
            year = mediaInfo.ProductionYear;
            seasons = new[]
            {
                new Season
                {
                    number = mediaInfo.ParentIndexNumber,
                    episodes = new[]
                    {
                        new ShowEpisode
                        {
                            number = mediaInfo.IndexNumber
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Gets or sets title.
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// Gets or sets year.
        /// </summary>
        public int? year { get; set; }

        /// <summary>
        /// Gets or sets seasons.
        /// </summary>
        public Season[] seasons { get; set; }

        /// <summary>
        /// Gets or sets ids.
        /// </summary>
        public override SimklIds ids { get; set; }
    }
}