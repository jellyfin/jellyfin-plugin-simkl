using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Simkl;

/// <summary>
/// Simkl external url provider.
/// </summary>
public class ExternalUrlProvider : IExternalUrlProvider
{
    private const string ClientId = "c721b22482097722a84a20ccc579cf9d232be85b9befe7b7805484d0ddbc6781";
    private const string BaseUrl = $"https://api.simkl.com/redirect?to=Simkl&client_id={ClientId}&";

    /// <inheritdoc />
    public string Name => "Simkl";

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is not Movie and not Series)
        {
            yield break;
        }

        if (item.TryGetProviderId(MetadataProvider.Imdb, out var imdbId))
        {
            yield return BaseUrl + $"imdb={imdbId}";
        }
        else if (item.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId))
        {
            yield return BaseUrl + $"tmdb={tmdbId}";
        }
        else if (item.TryGetProviderId(MetadataProvider.Tvdb, out var tvdbId))
        {
            yield return BaseUrl + $"tvdb={tvdbId}";
        }
        else if (item.TryGetProviderId("AniDB", out var aniDbId))
        {
            yield return BaseUrl + $"anidb={aniDbId}";
        }
        else if (item.TryGetProviderId("AniList", out var aniListId))
        {
            yield return BaseUrl + $"anilist={aniListId}";
        }
        else if (item.TryGetProviderId("AniSearch", out var aniSearchId))
        {
            yield return BaseUrl + $"anisearch={aniSearchId}";
        }
        else if (item.TryGetProviderId("Kitsu", out var kitsuId))
        {
            yield return BaseUrl + $"kitsu={kitsuId}";
        }
    }
}
