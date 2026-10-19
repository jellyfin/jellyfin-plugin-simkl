using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// The answer of POST /search/file/; Simkl answers an empty array when it recognizes nothing.
    /// </summary>
    public class SearchFileResponse
    {
        /// <summary>
        /// Gets or sets what was recognized: movie or episode.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets the episode.
        /// </summary>
        [JsonPropertyName("episode")]
        public SimklEpisode? Episode { get; set; }

        /// <summary>
        /// Gets or sets the movie.
        /// </summary>
        [JsonPropertyName("movie")]
        public SimklMovie? Movie { get; set; }

        /// <summary>
        /// Gets or sets the show of the episode.
        /// </summary>
        [JsonPropertyName("show")]
        public SimklShow? Show { get; set; }
    }
}
