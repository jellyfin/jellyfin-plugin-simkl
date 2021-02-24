using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl media object.
    /// </summary>
    public class SimklMediaObject
    {
        /// <summary>
        /// Gets or sets ids.
        /// </summary>
        [JsonPropertyName("ids")]
        public SimklIds? Ids { get; set; }
    }
}
