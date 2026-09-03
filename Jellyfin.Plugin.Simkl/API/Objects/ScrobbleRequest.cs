using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Request body for /scrobble/start, /scrobble/pause, and /scrobble/stop.
    /// Exactly one of <see cref="Movie"/> or (<see cref="Show"/> + <see cref="Episode"/>) must be set.
    /// </summary>
    public class ScrobbleRequest
    {
        /// <summary>
        /// Gets or sets the playback progress as a percentage (0–100).
        /// </summary>
        [JsonPropertyName("progress")]
        public float Progress { get; set; }

        /// <summary>
        /// Gets or sets the movie. Set for movie scrobbles.
        /// </summary>
        [JsonPropertyName("movie")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScrobbleMovie? Movie { get; set; }

        /// <summary>
        /// Gets or sets the show. Set for episode scrobbles together with <see cref="Episode"/>.
        /// </summary>
        [JsonPropertyName("show")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScrobbleShow? Show { get; set; }

        /// <summary>
        /// Gets or sets the episode. Set for episode scrobbles together with <see cref="Show"/>.
        /// </summary>
        [JsonPropertyName("episode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScrobbleEpisode? Episode { get; set; }
    }
}
