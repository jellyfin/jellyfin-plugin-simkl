using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Objects;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// One list entry of a Simkl library: a movie, a show or an anime with the episodes it has watched.
    /// </summary>
    public class AllItemsEntry
    {
        /// <summary>
        /// Gets or sets the list the entry sits in: watching, completed, hold, dropped, notinteresting or plantowatch.
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>
        /// Gets or sets when anything of this entry was last watched.
        /// </summary>
        [JsonPropertyName("last_watched_at")]
        public string? LastWatchedAt { get; set; }

        /// <summary>
        /// Gets or sets the anime type; "movie" means the entry is a movie even though it carries a show.
        /// </summary>
        [JsonPropertyName("anime_type")]
        public string? AnimeType { get; set; }

        /// <summary>
        /// Gets or sets the TVDB seasons this entry maps to.
        /// </summary>
        [JsonPropertyName("mapped_tvdb_seasons")]
        public IReadOnlyList<int>? MappedTvdbSeasons { get; set; }

        /// <summary>
        /// Gets or sets the show, on a show or anime entry.
        /// </summary>
        [JsonPropertyName("show")]
        public SimklShow? Show { get; set; }

        /// <summary>
        /// Gets or sets the movie, on a movie entry.
        /// </summary>
        [JsonPropertyName("movie")]
        public SimklMovie? Movie { get; set; }

        /// <summary>
        /// Gets or sets the watched seasons.
        /// </summary>
        [JsonPropertyName("seasons")]
        public IReadOnlyList<AllItemsSeason>? Seasons { get; set; }
    }
}
