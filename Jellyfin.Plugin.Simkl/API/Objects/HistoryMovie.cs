using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// A movie in a history request, with the date it was watched.
    /// </summary>
    /// <remarks>
    /// Deliberately not derived from <see cref="SimklMovie"/>: that one carries a typed watched_at for the sync
    /// endpoints, where a history request states the date as the exact string Simkl is to store it under.
    /// </remarks>
    public class HistoryMovie
    {
        /// <summary>
        /// Gets or sets the movie title, used when the ids identify nothing.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the release year, which goes with the title.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the ids of the movie.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklIds? Ids { get; set; }

        /// <summary>
        /// Gets or sets when it was watched, as "yyyy-MM-ddTHH:mm:ssZ".
        /// </summary>
        [JsonPropertyName("watched_at")]
        public string? WatchedAt { get; set; }
    }
}
