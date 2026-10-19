using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Mirrors play state a user changed by hand in Jellyfin to the Simkl history.
    /// </summary>
    /// <remarks>
    /// "Mark played" done by hand (a movie, an episode, a whole season or series) goes to Simkl's history under the same
    /// rules as scrobbling: the user is logged in and the movie or show checkbox is on. Playback and the plugin's own
    /// import save user data with other reasons and stay with the scrobbler and the importer. A single unmark is mirrored
    /// as a removal from the Simkl history; several unmarks at once (a season, a series, a clean-up) are deliberately not.
    /// </remarks>
    public class WatchedMarks : IHostedService
    {
        private const int HoldMs = 2000;        // a season or series mark arrives as one event per episode; one request carries them all
        private const int RetryDelayMs = 21000; // Simkl's per-user history lock is 20 seconds long

        private readonly IUserDataManager _userDataManager;
        private readonly IUserManager _userManager;
        private readonly ILogger<WatchedMarks> _logger;
        private readonly SimklApi _api;
        private readonly ConcurrentDictionary<Guid, Batch> _batches = new ConcurrentDictionary<Guid, Batch>();

        /// <summary>
        /// Initializes a new instance of the <see cref="WatchedMarks"/> class.
        /// </summary>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{WatchedMarks}"/> interface.</param>
        /// <param name="api">Instance of the <see cref="SimklApi"/>.</param>
        public WatchedMarks(
            IUserDataManager userDataManager,
            IUserManager userManager,
            ILogger<WatchedMarks> logger,
            SimklApi api)
        {
            _userDataManager = userDataManager;
            _userManager = userManager;
            _logger = logger;
            _api = api;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            return Task.CompletedTask;
        }

        // one request for the whole batch: movies with their date; episodes under their show with season, number and their own
        // TVDB/AniDB ids (matched within the show, number as the fallback, anime seasons mapped server-side); the bare episode
        // route only for an episode Jellyfin cannot place in a show
        private static HistoryRequest Build(List<Mark> marks, out int skipped)
        {
            var request = new HistoryRequest();
            var shows = new Dictionary<Guid, HistoryShow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);   // alternate versions of one item arrive as separate marks
            skipped = 0;

            foreach (var mark in marks)
            {
                var watchedAt = HistoryImporter.Stamp(mark.WatchedAt);
                if (mark.Item is Movie movie)
                {
                    var ids = new SimklIds(movie.ProviderIds);
                    if (!seen.Add("m" + (ids.Imdb ?? ids.Tmdb ?? "#" + movie.Id.ToString("N", CultureInfo.InvariantCulture))))
                    {
                        continue;
                    }

                    request.Movies.Add(new HistoryMovie
                    {
                        // empty as well as null: plenty of libraries store an empty original title, and sending
                        // that leaves Simkl nothing to match on when the ids identify nothing either
                        Title = string.IsNullOrEmpty(movie.OriginalTitle) ? movie.Name : movie.OriginalTitle,
                        Year = movie.ProductionYear,
                        Ids = ids,
                        WatchedAt = watchedAt
                    });
                    continue;
                }

                if (mark.Item is not Episode episode)
                {
                    continue;
                }

                var series = episode.Series;
                if (series is null || episode.ParentIndexNumber is null || episode.IndexNumber is null)
                {
                    // without a show and a number the episode id is the only route left; on that top-level route the server reads
                    // an anidb as an ANIME id and would complete a whole other series, so only the TVDB episode id travels there
                    var tvdb = new HistoryEpisodeIds(episode.ProviderIds).Tvdb;
                    if (tvdb is null)
                    {
                        skipped++;
                    }
                    else if (seen.Add("e" + tvdb))
                    {
                        request.Episodes.Add(new HistoryEpisode
                        {
                            WatchedAt = watchedAt,
                            Ids = new HistoryEpisodeIds(episode.ProviderIds) { Anidb = null }
                        });
                    }

                    continue;
                }

                if (!shows.TryGetValue(series.Id, out var show))
                {
                    show = new HistoryShow { Title = series.Name, Year = series.ProductionYear, Ids = new SimklIds(series.ProviderIds) };
                    shows[series.Id] = show;
                    request.Shows.Add(show);
                }

                var seasonNumber = episode.ParentIndexNumber.Value;
                var season = show.Seasons.Find(s => s.Number == seasonNumber);
                if (season is null)
                {
                    season = new HistorySeason { Number = seasonNumber };
                    show.Seasons.Add(season);
                }

                // under its show the entry carries the episode ids as well: the server matches them within that show and falls back
                // to the season and number (anime seasons are mapped there too), so an id Simkl lacks costs nothing;
                // the ids name the first episode of the file, the rest of a multi-episode file goes by number alone
                var episodeIds = new HistoryEpisodeIds(episode.ProviderIds);
                var first = episode.IndexNumber.Value;
                var last = Math.Max(first, episode.IndexNumberEnd ?? 0);   // a multi-episode file covers a range
                for (var number = first; number <= last; number++)
                {
                    var key = "s" + series.Id.ToString("N", CultureInfo.InvariantCulture)
                        + ":" + seasonNumber.ToString(CultureInfo.InvariantCulture)
                        + ":" + number.ToString(CultureInfo.InvariantCulture);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    season.Episodes.Add(new HistoryEpisode
                    {
                        Number = number,
                        WatchedAt = watchedAt,
                        Ids = number == first && (episodeIds.Tvdb is not null || episodeIds.Anidb is not null) ? episodeIds : null
                    });
                }
            }

            return request;
        }

        // Jellyfin reads dates back from its database with an unspecified kind; they are stored in UTC
        private static DateTime AsUtc(DateTime? value)
        {
            if (value is null)
            {
                return DateTime.UtcNow;
            }

            return value.Value.Kind switch
            {
                DateTimeKind.Utc => value.Value,
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            };
        }

        // raised inline by Jellyfin's save: it has to stay quick and must never throw
        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            try
            {
                if (e.SaveReason != UserDataSaveReason.TogglePlayed || e.UserData is null || e.Item is null || e.UserId.Equals(default))
                {
                    return;
                }

                var item = e.Item;

                var config = SimklPlugin.Instance?.Configuration.GetByGuid(e.UserId);
                var played = e.UserData.Played;
                var eligible = (item is Movie || item is Episode)
                    && config is not null
                    && !string.IsNullOrEmpty(config.UserToken)
                    && config.SyncWatchedMarks
                    && PlaybackScrobbler.CanBeScrobbled(config, item);
                if (played && !eligible)
                {
                    return;
                }

                var batch = _batches.GetOrAdd(e.UserId, _ => new Batch());
                CancellationTokenSource pending;
                lock (batch)
                {
                    // Every unmark counts towards "several at once", eligible or not: a season or series unmark fires
                    // one event per episode, and a short or excluded episode must not make the rest look single.
                    // Emby also flagged the folder's own event, but Jellyfin's Folder.MarkUnplayed only walks the leaf
                    // children and never saves user data for the folder itself, so no such event exists here. The
                    // consequence is that unmarking a season holding exactly ONE episode is indistinguishable from
                    // unmarking that episode on its own, and is mirrored. Guessing from "the parent has no played
                    // children left" would break the deliberate single-episode unmark instead, so it is left alone.
                    if (!played)
                    {
                        batch.Unmarked.Add(item.Id);
                    }

                    if (eligible)
                    {
                        // a mark and an unmark of the same item inside one hold cancel out (an accidental click): neither is sent
                        var opposite = batch.Marks.FindIndex(m => m.Item.Id.Equals(item.Id) && m.Played != played);
                        if (opposite >= 0)
                        {
                            batch.Marks.RemoveAt(opposite);
                        }
                        else
                        {
                            batch.Marks.Add(new Mark { Item = item, Played = played, WatchedAt = AsUtc(e.UserData.LastPlayedDate) });
                        }
                    }

                    batch.Pending?.Cancel();
                    pending = batch.Pending = new CancellationTokenSource();
                }

                _ = Flush(e.UserId, batch, pending);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Couldn't queue a watched mark for Simkl");
            }
        }

        // waits for the burst to end, then sends everything collected for the user in one request
        private async Task Flush(Guid userId, Batch batch, CancellationTokenSource pending)
        {
            // resolved before the work, so the two lines an admin actually has to act on name the user, not a raw guid
            var userName = _userManager.GetUserById(userId)?.Username ?? userId.ToString("N", CultureInfo.InvariantCulture);
            try
            {
                try
                {
                    await Task.Delay(HoldMs, pending.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;     // a newer mark restarted the hold and flushes for both
                }

                List<Mark> marks;
                bool bulkUnmark;
                lock (batch)
                {
                    if (batch.Pending != pending)
                    {
                        return;
                    }

                    marks = new List<Mark>(batch.Marks);
                    bulkUnmark = batch.Unmarked.Count > 1;
                    batch.Marks.Clear();
                    batch.Unmarked.Clear();
                    batch.Pending = null;
                }

                // re-read: the user may have logged out, or switched the mirroring off, during the hold
                var config = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
                if (config is null || string.IsNullOrEmpty(config.UserToken) || !config.SyncWatchedMarks)
                {
                    return;
                }

                await batch.Sending.WaitAsync().ConfigureAwait(false);
                try
                {
                    await SendMarks(userName, config, marks.Where(m => m.Played).ToList(), false).ConfigureAwait(false);

                    // an unmark is mirrored only when it stands alone: several at once are a season, a series or a clean-up in
                    // Jellyfin, not something to wipe from a Simkl history
                    var unmarks = marks.Where(m => !m.Played).ToList();
                    if (unmarks.Count > 0 && bulkUnmark)
                    {
                        _logger.LogInformation("{UserName} unmarked several items at once in Jellyfin; only a single unmark is sent to Simkl", userName);
                    }
                    else
                    {
                        await SendMarks(userName, config, unmarks, true).ConfigureAwait(false);
                    }
                }
                finally
                {
                    batch.Sending.Release();
                }
            }
            catch (InvalidTokenException)
            {
                _logger.LogInformation("Deleted the Simkl token of {UserName}", userName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Couldn't mark watched items on Simkl for {UserName}", userName);
            }
        }

        private async Task SendMarks(string userName, UserConfig config, List<Mark> marks, bool remove)
        {
            var request = Build(marks, out var skipped);
            var count = request.Movies.Count + request.Episodes.Count + request.Shows.Sum(s => s.Seasons.Sum(x => x.Episodes.Count));
            if (count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Simkl history: {Action} {Count} items {UserName} {Change} in Jellyfin",
                remove ? "removing" : "sending",
                count,
                userName,
                remove ? "unmarked" : "marked as watched");

            // only worth a line when something really was dropped
            if (skipped > 0)
            {
                _logger.LogInformation("{Skipped} of them had no usable ids and were skipped", skipped);
            }

            var response = await Deliver(request, config, userName, remove).ConfigureAwait(false);
            var counts = remove ? response?.Deleted : response?.Added;
            var notFound = (response?.NotFound?.Movies?.Count ?? 0)
                + (response?.NotFound?.Shows?.Count ?? 0)
                + (response?.NotFound?.Episodes?.Count ?? 0);
            _logger.LogInformation(
                "Simkl answered for {UserName}: {Movies} movies and {Episodes} episodes {Change}, {NotFound} not found on Simkl",
                userName,
                counts?.Movies ?? 0,
                counts?.Episodes ?? 0,
                remove ? "removed" : "added",
                notFound);
        }

        // Simkl answers 400 RATE_LIMIT while its 20-second per-user lock is held and may be briefly unreachable: one retry,
        // then the marks are given up with the error in the log
        private async Task<SyncHistoryResponse?> Deliver(HistoryRequest request, UserConfig config, string userName, bool remove)
        {
            try
            {
                return await Call(request, config, remove).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogInformation(
                    "Simkl rejected the history update for {UserName} ({Status}), retrying once in 21s",
                    userName,
                    ex.StatusCode.HasValue ? ((int)ex.StatusCode.Value).ToString(CultureInfo.InvariantCulture) : ex.Message);
                await Task.Delay(RetryDelayMs).ConfigureAwait(false);
                return await Call(request, config, remove).ConfigureAwait(false);
            }
        }

        private Task<SyncHistoryResponse?> Call(HistoryRequest request, UserConfig config, bool remove)
        {
            return remove ? _api.RemoveFromHistory(request, config) : _api.AddToHistory(request, config);
        }

        private sealed class Batch
        {
            public List<Mark> Marks { get; } = new List<Mark>();             // touched under lock (this)

            public HashSet<Guid> Unmarked { get; } = new HashSet<Guid>();    // every item unmarked in this hold, eligible or not

            public SemaphoreSlim Sending { get; } = new SemaphoreSlim(1, 1); // one request per user at a time

            public CancellationTokenSource? Pending { get; set; }
        }

        private sealed class Mark
        {
            public BaseItem Item { get; set; } = null!;

            // false = unmarked
            public bool Played { get; set; }

            public DateTime WatchedAt { get; set; }
        }
    }
}
