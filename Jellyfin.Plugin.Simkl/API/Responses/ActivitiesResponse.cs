using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The answer of GET /sync/activities; every value is "yyyy-MM-ddTHH:mm:ssZ" or null.
    /// Only the watch related keys are declared.
    /// </summary>
    public class ActivitiesResponse
    {
        /// <summary>
        /// Gets or sets the newest activity of any kind, on Simkl's clock: the watermark of the next incremental call.
        /// </summary>
        [JsonPropertyName("all")]
        public string? All { get; set; }

        /// <summary>
        /// Gets or sets the tv show lists.
        /// </summary>
        [JsonPropertyName("tv_shows")]
        public ActivitiesBucket? TvShows { get; set; }

        /// <summary>
        /// Gets or sets the anime lists.
        /// </summary>
        [JsonPropertyName("anime")]
        public ActivitiesBucket? Anime { get; set; }

        /// <summary>
        /// Gets or sets the movie lists.
        /// </summary>
        [JsonPropertyName("movies")]
        public ActivitiesBucket? Movies { get; set; }
    }
}
