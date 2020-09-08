using System.Text.Json.Serialization;
#pragma warning disable SA1300

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl episode container.
    /// </summary>
    public class SimklEpisode : SimklMediaObject
    {
        /// <summary>
        /// Gets or sets watched at.
        /// </summary>
        [JsonPropertyName("watched_at")]
        public string watched_at { get; set; }

        /// <summary>
        /// Gets or sets ids.
        /// </summary>
        public override SimklIds ids { get; set; }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// Gets or sets the season.
        /// </summary>
        public int season { get; set; }

        /// <summary>
        /// Gets or sets the episode.
        /// </summary>
        public int episode { get; set; }

        /// <summary>
        /// Gets or sets multipart.
        /// </summary>
        public bool? multipart { get; set; }
    }
}