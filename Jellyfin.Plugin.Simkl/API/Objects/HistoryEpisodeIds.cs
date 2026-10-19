using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// The ids of a single episode in a history request: only the two Simkl matches an episode by.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="SimklEpisodeIds"/>, which carries every id its base knows. For an episode
    /// Jellyfin also holds the ids of the show it belongs to, and offering those as episode ids is how an episode
    /// ends up matched to its series instead of to itself. Both are kept as strings: these are passed through, never
    /// compared as numbers.
    /// </remarks>
    public class HistoryEpisodeIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryEpisodeIds"/> class.
        /// </summary>
        /// <remarks>
        /// Needed because this type is read as well as written: the not_found of a history answer names the
        /// episodes Simkl could not place, and the deserializer has no provider ids to offer the other
        /// constructor.
        /// </remarks>
        public HistoryEpisodeIds()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryEpisodeIds"/> class.
        /// </summary>
        /// <param name="providerIds">The provider ids of the episode, may be null.</param>
        public HistoryEpisodeIds(IReadOnlyDictionary<string, string>? providerIds)
        {
            if (providerIds == null)
            {
                return;
            }

            foreach (var (key, value) in providerIds)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (key.Equals("Tvdb", StringComparison.OrdinalIgnoreCase))
                {
                    Tvdb = value;
                }
                else if (key.Equals("AniDB", StringComparison.OrdinalIgnoreCase))
                {
                    Anidb = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the TVDB episode id.
        /// </summary>
        [JsonPropertyName("tvdb")]
        public string? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets the AniDB episode id.
        /// </summary>
        [JsonPropertyName("anidb")]
        public string? Anidb { get; set; }
    }
}
