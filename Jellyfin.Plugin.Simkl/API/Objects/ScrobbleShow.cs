using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Show sub-object for scrobble requests.
    /// </summary>
    public class ScrobbleShow
    {
        /// <summary>
        /// Gets or sets the show title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the production year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the show ids.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklShowIds? Ids { get; set; }
    }
}
