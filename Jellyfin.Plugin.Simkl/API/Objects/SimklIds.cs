using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl Ids.
    /// </summary>
    public class SimklIds
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklIds"/> class.
        /// </summary>
        public SimklIds()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklIds"/> class.
        /// </summary>
        /// <param name="providerIds">The provider ids.</param>
        public SimklIds(IReadOnlyDictionary<string, string> providerIds)
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

                if (key.Equals(nameof(Simkl), StringComparison.OrdinalIgnoreCase))
                {
                    // Provider ids are free-form strings. A non-numeric value used to throw a
                    // FormatException out of Convert.ToInt32 and abort the whole scrobble, so
                    // parsing is best effort.
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var simklId))
                    {
                        Simkl = simklId;
                    }
                }
                else if (key.Equals(nameof(Anidb), StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var anidbId))
                    {
                        Anidb = anidbId;
                    }
                }
                else if (key.Equals(nameof(Imdb), StringComparison.OrdinalIgnoreCase))
                {
                    Imdb = value;
                }
                else if (key.Equals(nameof(Tvdb), StringComparison.OrdinalIgnoreCase))
                {
                    Tvdb = value;
                }
                else if (key.Equals(nameof(Slug), StringComparison.OrdinalIgnoreCase))
                {
                    Slug = value;
                }
                else if (key.Equals(nameof(Netflix), StringComparison.OrdinalIgnoreCase))
                {
                    Netflix = value;
                }
                else if (key.Equals(nameof(Tmdb), StringComparison.OrdinalIgnoreCase))
                {
                    Tmdb = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the simkl id.
        /// </summary>
        [JsonPropertyName("simkl")]
        public int? Simkl { get; set; }

        /// <summary>
        /// Gets or sets the imdb id.
        /// </summary>
        [JsonPropertyName("imdb")]
        public string? Imdb { get; set; }

        /// <summary>
        /// Gets or sets the slug.
        /// </summary>
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }

        /// <summary>
        /// Gets or sets the netflix id.
        /// </summary>
        [JsonPropertyName("netflix")]
        public string? Netflix { get; set; }

        /// <summary>
        /// Gets or sets the TMDb id.
        /// </summary>
        [JsonPropertyName("tmdb")]
        public string? Tmdb { get; set; }

        /// <summary>
        /// Gets or sets the TVDB id.
        /// </summary>
        [JsonPropertyName("tvdb")]
        public string? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets the AniDB id.
        /// </summary>
        [JsonPropertyName("anidb")]
        public int? Anidb { get; set; }

        /// <summary>
        /// Gets a value indicating whether at least one id was resolved.
        /// </summary>
        /// <returns><c>true</c> when any id is set; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// An all-null ids object should be omitted from the request entirely so that Simkl falls
        /// back to matching on title and year instead of on an empty id set.
        /// </remarks>
        public bool HasAnyId()
        {
            return Simkl.HasValue
                   || Anidb.HasValue
                   || !string.IsNullOrEmpty(Imdb)
                   || !string.IsNullOrEmpty(Tvdb)
                   || !string.IsNullOrEmpty(Tmdb)
                   || !string.IsNullOrEmpty(Slug)
                   || !string.IsNullOrEmpty(Netflix);
        }
    }
}