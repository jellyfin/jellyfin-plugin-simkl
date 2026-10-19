using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The TVDB season and episode of an anime episode; raw database values that arrive as a number or a string.
    /// </summary>
    public class AllItemsTvdbCoords
    {
        /// <summary>
        /// Gets or sets the TVDB season.
        /// </summary>
        [JsonPropertyName("season")]
        public string? Season { get; set; }

        /// <summary>
        /// Gets or sets the TVDB episode.
        /// </summary>
        [JsonPropertyName("episode")]
        public string? Episode { get; set; }
    }
}
