using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Simkl.API.Objects;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Provider-id and season/episode lookups over the items one user can see, built once per import run.
    /// </summary>
    /// <remarks>
    /// Movies and shows (series with their episodes) are read separately and only on first use, so a run with movie
    /// changes alone never walks the episodes; <see cref="Prepare"/> reads up front what a run already knows it needs.
    /// </remarks>
    public class LibraryIndex
    {
        // Jellyfin provider key -> Simkl id name; tvdb only identifies series and episodes
        private static readonly (string Jellyfin, string Simkl)[] ProviderNames =
        {
            ("Tvdb", "tvdb"),
            ("Imdb", "imdb"),
            ("Tmdb", "tmdb"),
            ("AniDB", "anidb"),
            ("MyAnimeList", "mal"),
            ("AniList", "anilist")
        };

        private readonly ILibraryManager _libraryManager;
        private readonly User _user;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<string, BaseItem> _movies = new Dictionary<string, BaseItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Series> _series = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Episode> _episodesByCoords = new Dictionary<string, Episode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Episode> _episodesByTvdbId = new Dictionary<string, Episode>(StringComparer.OrdinalIgnoreCase);
        private bool _moviesRead;
        private bool _showsRead;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryIndex"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="user">The user whose library is indexed.</param>
        /// <param name="cancellationToken">The cancellation token of the run.</param>
        public LibraryIndex(ILibraryManager libraryManager, User user, CancellationToken cancellationToken)
        {
            _libraryManager = libraryManager;
            _user = user;
            _cancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets the number of movies read from the library.
        /// </summary>
        public int MovieCount { get; private set; }

        /// <summary>
        /// Gets the number of series read from the library.
        /// </summary>
        public int SeriesCount { get; private set; }

        /// <summary>
        /// Gets the number of episodes read from the library.
        /// </summary>
        public int EpisodeCount { get; private set; }

        /// <summary>
        /// Reads what the types at hand need, so the first lookup does not pay for it.
        /// </summary>
        /// <param name="types">Simkl's buckets: movies, shows, anime (which holds shows and movies).</param>
        public void Prepare(IEnumerable<string> types)
        {
            var movies = false;
            var shows = false;
            foreach (var type in types)
            {
                movies |= string.Equals(type, "movies", StringComparison.Ordinal) || string.Equals(type, "anime", StringComparison.Ordinal);
                shows |= string.Equals(type, "shows", StringComparison.Ordinal) || string.Equals(type, "anime", StringComparison.Ordinal);
            }

            if (movies)
            {
                ReadMovies();
            }

            if (shows)
            {
                ReadShows();
            }
        }

        /// <summary>
        /// Describes what was read, for the run log.
        /// </summary>
        /// <returns>A human readable summary of the index.</returns>
        public string Describe()
        {
            var movies = _moviesRead
                ? MovieCount.ToString(CultureInfo.InvariantCulture) + " movies"
                : "no movies (not needed)";
            var shows = _showsRead
                ? SeriesCount.ToString(CultureInfo.InvariantCulture) + " series, " + EpisodeCount.ToString(CultureInfo.InvariantCulture) + " episodes"
                : "no series (not needed)";
            return "indexed " + movies + ", " + shows;
        }

        /// <summary>
        /// Lists what every indexed series is keyed by.
        /// </summary>
        /// <remarks>
        /// For telling a show the library does not have apart from one it has under ids Simkl does not use, which
        /// look identical in the count of what was not matched.
        /// </remarks>
        /// <returns>One entry per series, naming it and the ids it can be found by.</returns>
        public string DescribeSeriesIndex()
        {
            // only what was read: a run that never needed the series has nothing unmatched to explain
            if (!_showsRead)
            {
                return "no series (not needed)";
            }

            return string.Join(
                "; ",
                _series.GroupBy(pair => pair.Value, pair => pair.Key)
                    .Select(group => group.Key.Name + " [" + string.Join(" ", group) + "]"));
        }

        /// <summary>
        /// Finds the movie a Simkl entry names.
        /// </summary>
        /// <param name="ids">The ids Simkl knows the movie by.</param>
        /// <returns>The library item, or null when the movie is not in the library.</returns>
        public BaseItem? FindMovie(SimklIds ids)
        {
            ReadMovies();
            return Find(_movies, ids, false, out _);
        }

        /// <summary>
        /// Finds the series a Simkl entry names.
        /// </summary>
        /// <param name="ids">The ids Simkl knows the show by.</param>
        /// <param name="matchedBy">
        /// Which id matched: tvdb, imdb and tmdb mean the library numbers the episodes the way TVDB does,
        /// anidb, mal and anilist mean it numbers them absolutely.
        /// </param>
        /// <returns>The series, or null when the show is not in the library.</returns>
        public Series? FindSeries(SimklIds ids, out string? matchedBy)
        {
            ReadShows();
            return Find(_series, ids, true, out matchedBy);
        }

        /// <summary>
        /// Finds an episode by its place in a series.
        /// </summary>
        /// <param name="seriesId">The id of the series in the library.</param>
        /// <param name="season">The season number, 0 for specials.</param>
        /// <param name="number">The episode number within that season.</param>
        /// <returns>The episode, or null when it is not in the library.</returns>
        public Episode? FindEpisode(Guid seriesId, int season, int number)
        {
            ReadShows();
            return Get(_episodesByCoords, Coord(seriesId, season, number));
        }

        /// <summary>
        /// Finds an episode by its own TVDB id, wherever it sits in the library.
        /// </summary>
        /// <param name="tvdbId">The TVDB episode id.</param>
        /// <returns>The episode, or null when no episode carries that id.</returns>
        public Episode? FindEpisodeByTvdbId(int tvdbId)
        {
            ReadShows();
            return Get(_episodesByTvdbId, "tvdb:" + tvdbId.ToString(CultureInfo.InvariantCulture));
        }

        private static string Coord(Guid seriesId, int season, int number)
        {
            return seriesId.ToString("N", CultureInfo.InvariantCulture)
                + ":" + season.ToString(CultureInfo.InvariantCulture)
                + ":" + number.ToString(CultureInfo.InvariantCulture);
        }

        private static IEnumerable<string> ProviderKeys(BaseItem item, bool withTvdb)
        {
            var ids = item.ProviderIds;
            if (ids is null)
            {
                yield break;
            }

            foreach (var (jellyfin, simkl) in ProviderNames)
            {
                if (!withTvdb && string.Equals(simkl, "tvdb", StringComparison.Ordinal))
                {
                    continue;
                }

                // ProviderIds is an OrdinalIgnoreCase dictionary, so a plugin's own spelling of the key still matches
                if (ids.TryGetValue(jellyfin, out var value) && !string.IsNullOrEmpty(value))
                {
                    yield return simkl + ":" + value;
                }
            }
        }

        private static void Add<T>(Dictionary<string, T> index, T item, IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                Put(index, key, item);
            }
        }

        // first item wins so duplicates in the library stay deterministic
        private static void Put<T>(Dictionary<string, T> index, string key, T item)
        {
            if (!index.ContainsKey(key))
            {
                index[key] = item;
            }
        }

        private static T? Get<T>(Dictionary<string, T> index, string key)
            where T : class
        {
            return index.TryGetValue(key, out var item) ? item : null;
        }

        private static T? Find<T>(Dictionary<string, T> index, SimklIds ids, bool withTvdb, out string? matchedBy)
            where T : class
        {
            foreach (var (provider, value) in SimklKeys(ids, withTvdb))
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var item = Get(index, provider + ":" + value);
                if (item is null)
                {
                    continue;
                }

                matchedBy = provider;
                return item;
            }

            matchedBy = null;
            return null;
        }

        private static IEnumerable<(string Provider, string? Value)> SimklKeys(SimklIds ids, bool withTvdb)
        {
            if (withTvdb)
            {
                yield return ("tvdb", ids.Tvdb);
            }

            yield return ("imdb", ids.Imdb);
            yield return ("tmdb", ids.Tmdb);

            // Simkl parses this one as a number; the index keys on strings throughout
            yield return ("anidb", ids.Anidb?.ToString(CultureInfo.InvariantCulture));
            yield return ("mal", ids.Mal);
            yield return ("anilist", ids.Anilist);
        }

        private void ReadMovies()
        {
            if (_moviesRead)
            {
                return;
            }

            _moviesRead = true;
            foreach (var item in Query(BaseItemKind.Movie))
            {
                MovieCount++;
                Add(_movies, item, ProviderKeys(item, false));
            }
        }

        private void ReadShows()
        {
            if (_showsRead)
            {
                return;
            }

            _showsRead = true;
            foreach (var item in Query(BaseItemKind.Series))
            {
                if (item is not Series series)
                {
                    continue;
                }

                SeriesCount++;
                Add(_series, series, ProviderKeys(series, true));
            }

            foreach (var item in Query(BaseItemKind.Episode))
            {
                if (item is not Episode episode)
                {
                    continue;
                }

                EpisodeCount++;
                AddEpisode(episode);
            }
        }

        private void AddEpisode(Episode episode)
        {
            var ids = episode.ProviderIds;
            if (ids is not null && ids.TryGetValue("Tvdb", out var tvdbId) && !string.IsNullOrEmpty(tvdbId))
            {
                Put(_episodesByTvdbId, "tvdb:" + tvdbId, episode);
            }

            if (episode.ParentIndexNumber is null || episode.IndexNumber is null || episode.SeriesId.Equals(default))
            {
                return;
            }

            var first = episode.IndexNumber.Value;
            var last = Math.Max(first, episode.IndexNumberEnd ?? first);    // a multi-episode file covers a range
            for (var number = first; number <= last; number++)
            {
                Put(_episodesByCoords, Coord(episode.SeriesId, episode.ParentIndexNumber.Value, number), episode);
            }
        }

        // With User set, GetItemList runs the query through the user's own views: the libraries that user may see and the
        // parental policy, the same primitive Jellyfin's own endpoints use. IsVirtualItem keeps missing and unaired
        // episodes out, so an import can never mark a placeholder as played.
        //
        // ProviderIds has to be asked for by name. It is not loaded unless a field asks for it, and without it every
        // item comes back with an empty id set, which is the whole of what this class indexes on: the import then
        // reports the entire library as missing and marks nothing. Images and user data stay off: images are the
        // expensive part, and user data loaded here is every user's rows on every item. The importer reads the one
        // user's record per item it marks, through the batch read that goes to the database for it; the plain
        // GetUserData(user, item) only looks at what the item carries, and an item from here carries nothing.
        private IReadOnlyList<BaseItem> Query(BaseItemKind kind)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return _libraryManager.GetItemList(new InternalItemsQuery(_user)
            {
                IncludeItemTypes = new[] { kind },
                Recursive = true,
                IsVirtualItem = false,
                EnableTotalRecordCount = false,
                DtoOptions = new DtoOptions(false)
                {
                    Fields = new[] { ItemFields.ProviderIds },
                    EnableImages = false,
                    EnableUserData = false
                }
            });
        }
    }
}
