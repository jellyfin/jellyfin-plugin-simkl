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
        public override SimklIds Ids { get; set; }

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the season.
        /// </summary>
        public int Season { get; set; }

        /// <summary>
        /// Gets or sets the episode.
        /// </summary>
        public int Episode { get; set; }

        /// <summary>
        /// Gets or sets multipart.
        /// </summary>
        public bool? Multipart { get; set; }
    }
}