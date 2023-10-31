using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl show ids.
    /// </summary>
    public class SimklShowIds : SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklShowIds"/> class.
        /// </summary>
        /// <param name="providerMovieIds">The provider movie ids.</param>
        public SimklShowIds(Dictionary<string, string> providerMovieIds)
            : base(providerMovieIds)
        {
        }

        /// <summary>
        /// Gets or sets mal.
        /// </summary>
        [JsonPropertyName("mal")]
        public int? Mal { get; set; }

        /// <summary>
        /// Gets or sets hulu.
        /// </summary>
        [JsonPropertyName("hulu")]
        public int? Hulu { get; set; }

        /// <summary>
        /// Gets or sets crunchyroll.
        /// </summary>
        [JsonPropertyName("crunchyroll")]
        public int? Crunchyroll { get; set; }

        /// <summary>
        /// Gets or sets zap2it.
        /// </summary>
        [JsonPropertyName("zap2It")]
        public string? Zap2It { get; set; }
    }
}