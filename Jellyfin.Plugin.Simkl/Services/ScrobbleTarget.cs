using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// The metadata a scrobble call is built from.
    /// </summary>
    /// <remarks>
    /// Episodes need two distinct id sets: Simkl matches the show from the series ids (or its
    /// title and year) and the episode from its season and number. Flattening both into a single
    /// <see cref="BaseItemDto.ProviderIds"/> sent the series ids as episode ids, which made Simkl
    /// fail to identify the episode.
    /// </remarks>
    internal sealed class ScrobbleTarget
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleTarget"/> class.
        /// </summary>
        /// <param name="item">The item to scrobble.</param>
        /// <param name="seriesProviderIds">Provider ids of the parent series, for episodes.</param>
        public ScrobbleTarget(BaseItemDto item, IReadOnlyDictionary<string, string>? seriesProviderIds)
        {
            Item = item;
            SeriesProviderIds = seriesProviderIds;
        }

        /// <summary>
        /// Gets the item to scrobble. For episodes, <see cref="BaseItemDto.ProviderIds"/> holds the
        /// episode's own ids and <see cref="BaseItemDto.ProductionYear"/> holds the series year.
        /// </summary>
        public BaseItemDto Item { get; }

        /// <summary>
        /// Gets the provider ids of the parent series, or <c>null</c> when there is no series or
        /// none could be resolved.
        /// </summary>
        public IReadOnlyDictionary<string, string>? SeriesProviderIds { get; }
    }
}
