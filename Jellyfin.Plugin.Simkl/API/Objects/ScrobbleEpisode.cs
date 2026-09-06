using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Episode sub-object for scrobble requests (show+episode pair).
    /// </summary>
    public class ScrobbleEpisode
    {
        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("season")]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number.
        /// </summary>
        [JsonPropertyName("number")]
        public int? Number { get; set; }

        /// <summary>
        /// Gets or sets the episode IDs.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklEpisodeIds? Ids { get; set; }
    }
}
