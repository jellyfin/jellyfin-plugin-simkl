using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// When each list of one type last changed. The dropped list is keyed "notinteresting" for older app ids
    /// and "dropped" for newer ones, so both are declared.
    /// </summary>
    public class ActivitiesBucket
    {
        /// <summary>
        /// Gets or sets when the watching list last changed.
        /// </summary>
        [JsonPropertyName("watching")]
        public string? Watching { get; set; }

        /// <summary>
        /// Gets or sets when the completed list last changed.
        /// </summary>
        [JsonPropertyName("completed")]
        public string? Completed { get; set; }

        /// <summary>
        /// Gets or sets when the on hold list last changed.
        /// </summary>
        [JsonPropertyName("hold")]
        public string? Hold { get; set; }

        /// <summary>
        /// Gets or sets when the dropped list last changed.
        /// </summary>
        [JsonPropertyName("dropped")]
        public string? Dropped { get; set; }

        /// <summary>
        /// Gets or sets when the dropped list last changed, under the name older app ids get.
        /// </summary>
        [JsonPropertyName("notinteresting")]
        public string? NotInteresting { get; set; }
    }
}
