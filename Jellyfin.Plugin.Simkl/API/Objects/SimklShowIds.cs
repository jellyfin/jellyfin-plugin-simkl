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
        public SimklShowIds(IReadOnlyDictionary<string, string> providerMovieIds)
            : base(providerMovieIds)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklShowIds"/> class with no ids.
        /// </summary>
        public SimklShowIds()
        {
        }

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