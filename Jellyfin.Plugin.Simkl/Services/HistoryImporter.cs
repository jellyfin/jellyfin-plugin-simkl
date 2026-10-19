using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Responses;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SimklIds = Jellyfin.Plugin.Simkl.API.Objects.SimklIds;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// One-way Simkl to Jellyfin watched import.
    /// </summary>
    /// <remarks>
    /// A manual run pulls the whole history; a scheduled run pulls only what /sync/activities says changed. The
    /// service is a singleton, so one run per user at a time is enforced in <see cref="_runs"/>.
    /// </remarks>
    public sealed class HistoryImporter
    {
        /// <summary>
        /// How many minutes must pass between two runs for the same user.
        /// </summary>
        public const int CooldownMinutes = 2;

        /// <summary>
        /// The stored summary of a run that is still going, so a run lost to a restart is reported as such.
        /// </summary>
        public const string InterruptedSummary = "Import failed: the server restarted before it finished, run it again";

        /// <summary>
        /// The summary of a run the scheduled task cancelled.
        /// </summary>
        public const string CancelledSummary = "Automatic import was stopped before it finished (the scheduled task was cancelled or the server was shutting down). It picks up from the same point on the next scheduled run, or when you press Import changes since the last import.";

        private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        // the activities row is saved before the watchlist row and date_from is a strict '>' on updateTime, so look back a little
        private const int DateFromSlackSeconds = 60;

        // This client_id predates the "dropped" rename, so the API spells that status "notinteresting" in responses
        private const string DroppedSegment = "notinteresting";

        private static readonly string[] Types = { "shows", "anime", "movies" };

        private readonly ConcurrentDictionary<Guid, ImportRun> _runs = new ConcurrentDictionary<Guid, ImportRun>();

        // finish time of the last run of any kind; kept apart from _runs because a new run replaces that entry
        private readonly ConcurrentDictionary<Guid, DateTime> _lastFinish = new ConcurrentDictionary<Guid, DateTime>();

        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ImportStateStore _store;
        private readonly SimklApi _api;
        private readonly ILogger<HistoryImporter> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoryImporter"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
        /// <param name="store">The import state store.</param>
        /// <param name="api">The Simkl api.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{HistoryImporter}"/> interface.</param>
        public HistoryImporter(
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            ImportStateStore store,
            SimklApi api,
            ILogger<HistoryImporter> logger)
        {
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _store = store;
            _api = api;
            _logger = logger;
        }

        /// <summary>
        /// Reads what the page shows: the live run when there is one, the stored result otherwise.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>The import status.</returns>
        public ImportStatus Status(User user)
        {
            _runs.TryGetValue(user.Id, out var run);
            return ToStatus(run, _store.Load(user.Id));
        }

        /// <summary>
        /// Starts a full import in the background, at most once per cooldown.
        /// </summary>
        /// <remarks>
        /// This is "Reset and re-import fully" and the plain POST route. <paramref name="reset"/> additionally forgets
        /// the import memory and refreshes the watch date of items Jellyfin already has as played.
        /// </remarks>
        /// <param name="user">The user.</param>
        /// <param name="config">The user's Simkl configuration.</param>
        /// <param name="reset">Whether to forget the cursors and refresh already played items.</param>
        /// <returns>The status the page polls from.</returns>
        public ImportStatus TryStart(User user, UserConfig config, bool reset = false)
        {
            if (RefusalReason(user.Id, config) is string why)
            {
                return Refused(user.Id, why);
            }

            var run = new ImportRun { Running = true, Manual = true, Reset = reset, Message = "Starting...", StartedUtc = DateTime.UtcNow };
            while (!_runs.TryAdd(user.Id, run))
            {
                if (!_runs.TryGetValue(user.Id, out var previous))
                {
                    continue;
                }

                if (previous.Running)
                {
                    // A second click joins the run it started, but only a run of the same kind: a check or a
                    // scheduled run is not joinable, and neither is a plain import when a reset was asked for,
                    // since joining it would report the reset as finished without anything having been reset.
                    return previous.Manual && previous.Reset == reset
                        ? ToStatus(previous, _store.Load(user.Id))
                        : Refused(user.Id, "An import is running right now, try again when it finishes");
                }

                if (_runs.TryUpdate(user.Id, run, previous))
                {
                    break;
                }
            }

            var state = Publish(user, run, config);
            _logger.LogInformation("Import for {UserName} started{Reset}", user.Username, reset ? " (reset)" : string.Empty);
            _ = Task.Run(() => RunAsync(user, config, state, run, true, CancellationToken.None), CancellationToken.None);
            return ToStatus(run, state);
        }

        /// <summary>
        /// The scheduled task path: incremental once a full import exists, otherwise the one-time full import.
        /// </summary>
        /// <remarks>
        /// The task ticks hourly; each user is checked only when their own interval has elapsed since their last
        /// automatic run. It is never queued behind a live run.
        /// </remarks>
        /// <param name="user">The user.</param>
        /// <param name="config">The user's Simkl configuration.</param>
        /// <param name="cancellationToken">The token the task is cancelled with.</param>
        /// <returns>The running import.</returns>
        public Task RunScheduled(User user, UserConfig config, CancellationToken cancellationToken)
        {
            if (RefusalReason(user.Id, config) != null)
            {
                return Task.CompletedTask;
            }

            var lastAuto = ParseDate(_store.Load(user.Id).LastAutoRunAt);
            var hours = Math.Max(1, config.AutoImportHours);
            if (lastAuto.HasValue && (DateTime.UtcNow - lastAuto.Value).TotalMinutes < (hours * 60) - 5)
            {
                return Task.CompletedTask;   // 5 min slack for tick drift
            }

            return StartAuto(user, config, true, cancellationToken) ?? Task.CompletedTask;
        }

        /// <summary>
        /// Runs the check the scheduled task would do, right away and in the background.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="config">The user's Simkl configuration.</param>
        /// <param name="scheduled">True when the automatic import toggle was just switched on, so the interval counts from here like from a task run.</param>
        /// <returns>The status the page polls from.</returns>
        public ImportStatus TryStartAuto(User user, UserConfig config, bool scheduled)
        {
            if (RefusalReason(user.Id, config) is string why)
            {
                return Refused(user.Id, why);
            }

            _ = StartAuto(user, config, scheduled, CancellationToken.None);
            return Status(user);
        }

        /// <summary>
        /// Formats a timestamp the way Simkl and the stored state spell it.
        /// </summary>
        /// <param name="utc">The UTC time.</param>
        /// <returns>The ISO timestamp.</returns>
        public static string Stamp(DateTime utc)
        {
            return utc.ToString(DateFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a Simkl timestamp.
        /// </summary>
        /// <param name="value">The ISO timestamp.</param>
        /// <returns>The UTC time, or null when it is missing or Simkl's "date unknown" sentinel.</returns>
        public static DateTime? ParseDate(string? value)
        {
            if (!DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            {
                return null;
            }

            return date.Year < 2000 ? null : date;
        }

        /// <summary>
        /// Identifies the Simkl login without storing a credential a second time.
        /// </summary>
        /// <param name="anchor">The login's <see cref="UserConfig.AccountAnchor"/>, never the access token.</param>
        /// <returns>The hash of it, or an empty string.</returns>
        public static string AccountKey(string? anchor)
        {
            return string.IsNullOrEmpty(anchor)
                ? string.Empty
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(anchor)));
        }

        // null when a run may start
        private string? RefusalReason(Guid userId, UserConfig config)
        {
            if (string.IsNullOrEmpty(config.UserToken))
            {
                return "Log in to Simkl first";
            }

            return CooldownMessage(userId);
        }

        // returns the running task, or null when another run for this user is active
        private Task? StartAuto(User user, UserConfig config, bool scheduled, CancellationToken cancellationToken)
        {
            var run = new ImportRun { Running = true, Scheduled = scheduled, Message = "Starting...", StartedUtc = DateTime.UtcNow };
            if (!_runs.TryAdd(user.Id, run))
            {
                if (!_runs.TryGetValue(user.Id, out var previous) || previous.Running || !_runs.TryUpdate(user.Id, run, previous))
                {
                    return null;
                }
            }

            var state = Publish(user, run, config);
            var full = string.IsNullOrEmpty(state.FullImportAt);
            _logger.LogInformation(
                "{Kind} for {UserName} started ({Mode})",
                scheduled ? "Automatic import" : "New-history check",
                user.Username,
                full ? "first run, full" : "incremental");
            return RunAsync(user, config, state, run, full, cancellationToken);
        }

        // the run already owns the _runs entry; retire it if the state cannot be read so it never stays "running"
        private ImportState Publish(User user, ImportRun run, UserConfig config)
        {
            try
            {
                return MarkStarted(user, run, config);
            }
            catch
            {
                _runs.TryRemove(user.Id, out _);
                throw;
            }
        }

        private ImportState MarkStarted(User user, ImportRun run, UserConfig config)
        {
            var state = _store.Load(user.Id);

            // Cursors belong to the login they were read under; a different one starts over with a full import.
            // Anchored on the grant rather than on the access token, which is renewed every few days, so this is
            // a real sign in and not a renewal. Signing in again to migrate a pin flow login off the old app is
            // therefore one full import, once - nothing is sent to Simkl by it, only the library is re-marked.
            var account = AccountKey(config.AccountAnchor);
            if (!run.Reset && !string.Equals(state.CursorAccount, account, StringComparison.Ordinal) && state.CursorAccount.Length > 0)
            {
                _logger.LogInformation("The Simkl login of {UserName} changed; importing the whole history once", user.Username);
            }

            if (run.Reset || !string.Equals(state.CursorAccount, account, StringComparison.Ordinal))
            {
                state.FullImportAt = string.Empty;
                state.SyncedAt = string.Empty;
                state.CursorShows = string.Empty;
                state.CursorAnime = string.Empty;
                state.CursorMovies = string.Empty;
            }

            // Stamped here, with the anchor the reset decision was just made on, and never read again when the run
            // finishes. The importer is handed the stored settings object itself, which a device login completing
            // mid-run replaces in place: re-reading the anchor at the end would file this run's cursors, which
            // belong to the old account, under the new one, and the next run would then see no change and go
            // incremental over history it has never read. A run that dies after this point leaves FullImportAt
            // empty, so the next one is full regardless.
            state.CursorAccount = account;

            // marker so a run lost to a server restart is reported instead of looking like the previous result
            state.LastImportAt = Stamp(run.StartedUtc);
            state.LastImportSummary = InterruptedSummary;
            if (run.Scheduled)
            {
                state.LastAutoRunAt = state.LastImportAt;   // set at start so a failing run also waits the interval
            }

            _store.Save(user.Id, user.Username, state);
            return state;
        }

        private async Task RunAsync(User user, UserConfig config, ImportState state, ImportRun run, bool full, CancellationToken cancellationToken)
        {
            var job = new Job(user, run.Reset);

            // Jellyfin's user data has no "last modified" stamp, so a play state the user changes while the run is
            // going is remembered here instead, and never overwritten by what Simkl says
            _userDataManager.UserDataSaved += job.OnUserDataSaved;
            try
            {
                // read activities before pulling so the saved cursor can never run ahead of the data it covers
                var activities = await _api.GetActivities(config, cancellationToken).ConfigureAwait(false);
                var cursors = Cursors(activities);
                var changed = full ? new List<string>(Types) : ChangedTypes(state, cursors);

                LibraryIndex? index = null;
                if (changed.Count > 0)
                {
                    run.Message = "Reading Jellyfin library...";
                    _logger.LogInformation("Import for {UserName}: reading Jellyfin library", user.Username);
                    index = new LibraryIndex(_libraryManager, user, cancellationToken);
                    index.Prepare(changed);   // a change confined to movies never reads the series and episodes
                    _logger.LogInformation("Import for {UserName}: {Index}", user.Username, index.Describe());
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Import for {UserName}: series index {Series}", user.Username, index.DescribeSeriesIndex());
                    }
                }

                if (index != null)
                {
                    // one call for everything updated since the activities read of the last successful run;
                    // the per-type cursors only decide whether to call
                    // FullImportAt is deliberately not a fallback: it is stamped from this server's clock and only
                    // says whether a full import has happened, where date_from is compared against Simkl's clock
                    var since = full ? null : ParseDate(state.SyncedAt);
                    await Pull(job, index, run, config, since.HasValue ? Stamp(since.Value.AddSeconds(-DateFromSlackSeconds)) : null, cancellationToken).ConfigureAwait(false);
                }

                foreach (var type in Types)
                {
                    if (cursors.TryGetValue(type, out var cursor) && cursor != null)
                    {
                        SetCursor(state, type, cursor);
                    }
                }

                // CursorAccount is deliberately not touched here; MarkStarted set it from the anchor this run
                // actually began under.
                if (full)
                {
                    state.FullImportAt = Stamp(DateTime.UtcNow);
                }

                // Simkl's own clock, read before the pull, so a change made during the run is caught next time.
                // Only ever Simkl's: this is compared against their timestamps, and an account that has never
                // touched a list has no activity row and is answered null. Standing in this server's clock there
                // would send a date_from on the wrong clock, and anything watched inside the difference would be
                // skipped for good, since the cursors move on regardless. No watermark means no date_from.
                if (!string.IsNullOrEmpty(activities?.All))
                {
                    state.SyncedAt = activities.All;
                }

                run.Message = (run.Reset ? "Everything re-imported. " : string.Empty) + Summary(job, changed.Count == 0);
                _logger.LogInformation("Import for {UserName}: {Message}", user.Username, run.Message);
            }
            catch (InvalidTokenException)
            {
                run.Error = "Simkl login expired, log in again";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // rethrown so Jellyfin records the scheduled task as cancelled; finally still persists the state
                run.Cancelled = true;
                run.Error = CancelledSummary;
                throw;
            }
            catch (OperationCanceledException)
            {
                // HttpClient reports its own timeout as a cancellation, not as an HttpRequestException
                run.Error = "Simkl did not answer in time (the request timed out)";
            }
            catch (HttpRequestException ex)
            {
                run.Error = ex.StatusCode.HasValue
                    ? FormattableString.Invariant($"Simkl answered HTTP {(int)ex.StatusCode.Value}")
                    : "Simkl did not answer (unreachable): " + ex.Message;
            }
            catch (JsonException ex)
            {
                run.Error = "Simkl sent an answer the plugin could not read: " + ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import for {UserName} failed", user.Username);
                run.Error = ex.Message;
            }
            finally
            {
                _userDataManager.UserDataSaved -= job.OnUserDataSaved;
                Finish(user, state, run);
            }
        }

        // one call for everything Simkl has, every type and every list; Simkl builds that response in memory and refuses one that
        // is too big with HTTP 400 MAX_ITEMS, which ends the run with that error (by decision, no splitting into smaller calls)
        private async Task Pull(Job job, LibraryIndex index, ImportRun run, UserConfig config, string? dateFrom, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = dateFrom == null ? "everything" : "changes";
            run.Message = "Fetching " + label + "...";
            _logger.LogInformation("Import for {UserName}: fetching {Label}", job.User.Username, label);
            var response = await Fetch(dateFrom, config, cancellationToken).ConfigureAwait(false);
            run.Message = "Marking movies and episodes...";
            foreach (var type in Types)
            {
                Apply(job, index, type, response, cancellationToken);
            }
        }

        private async Task<AllItemsResponse> Fetch(string? dateFrom, UserConfig config, CancellationToken cancellationToken)
        {
            // full_anime_seasons is "full" plus the TVDB coordinates of anime episodes; completed shows only carry episodes with
            // include_all_episodes, as virtual rows dated with the show's last watch
            var query = "extended=full_anime_seasons&episode_watched_at=yes&episode_tvdb_id=yes&include_all_episodes=yes"
                + (dateFrom == null ? string.Empty : "&date_from=" + Uri.EscapeDataString(dateFrom));
            var backoffMs = 1000;
            while (true)
            {
                try
                {
                    return await _api.GetAllItems(query, config, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (backoffMs > 16000)
                    {
                        throw new InvalidOperationException("Simkl is rate limiting requests, try again later", ex);
                    }

                    _logger.LogInformation("Simkl rate limited the import, retrying in {Seconds}s", backoffMs / 1000);
                    await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false);
                    backoffMs *= 2;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
                {
                    // 400 on this route is MAX_ITEMS: the history is too big for Simkl to answer in one response
                    throw new InvalidOperationException("Simkl refused the import: this history is too large for one request", ex);
                }
            }
        }

        private void Apply(Job job, LibraryIndex index, string bucket, AllItemsResponse response, CancellationToken cancellationToken)
        {
            var movies = string.Equals(bucket, "movies", StringComparison.Ordinal);
            var entries = movies ? response.Movies : string.Equals(bucket, "anime", StringComparison.Ordinal) ? response.Anime : response.Shows;
            var movieMarks = new List<(BaseItem Item, DateTime? Own, DateTime? Fallback)>();
            foreach (var entry in entries ?? Array.Empty<AllItemsEntry>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entryStatus = Normalize(entry.Status);
                if (string.Equals(entryStatus, "plantowatch", StringComparison.Ordinal))
                {
                    continue;   // never a watch
                }

                // a movie is watched only when completed; hold, dropped and watching anime movies carry no watch
                if (!movies && !string.Equals(entry.AnimeType, "movie", StringComparison.Ordinal))
                {
                    ApplyShow(job, index, entry, string.Equals(bucket, "anime", StringComparison.Ordinal), cancellationToken);
                }
                else if (string.Equals(entryStatus, "completed", StringComparison.Ordinal))
                {
                    ApplyMovie(job, index, entry, movieMarks);
                }
            }

            job.MoviesMarked += MarkPlayed(job, movieMarks);
        }

        private void ApplyMovie(Job job, LibraryIndex index, AllItemsEntry entry, List<(BaseItem Item, DateTime? Own, DateTime? Fallback)> marks)
        {
            var ids = entry.Movie?.Ids ?? entry.Show?.Ids;   // anime movies carry their ids under show
            var item = ids == null ? null : index.FindMovie(ids);
            if (item == null)
            {
                job.MoviesNotInLibrary++;
                _logger.LogDebug("Not in library: movie {Title} {Ids}", entry.Movie?.Title ?? entry.Show?.Title, Describe(ids));
                return;
            }

            marks.Add((item, ParseDate(entry.LastWatchedAt), null));
        }

        private void ApplyShow(Job job, LibraryIndex index, AllItemsEntry entry, bool anime, CancellationToken cancellationToken)
        {
            var ids = entry.Show?.Ids;
            var title = entry.Show?.Title;
            string? matchedBy = null;
            var series = ids == null ? null : index.FindSeries(ids, out matchedBy);
            var absolute = string.Equals(matchedBy, "anidb", StringComparison.Ordinal)
                || string.Equals(matchedBy, "mal", StringComparison.Ordinal)
                || string.Equals(matchedBy, "anilist", StringComparison.Ordinal);
            var fallbackDate = ParseDate(entry.LastWatchedAt);
            var seasons = entry.Seasons ?? Array.Empty<AllItemsSeason>();

            // only a completed show gets synthesized rows, all carrying the show's last_watched_at; the
            // episode really watched last shares that date too, so there a date several episodes share counts as synthesized
            var synthesized = string.Equals(Normalize(entry.Status), "completed", StringComparison.Ordinal)
                && seasons.Sum(s => (s.Episodes ?? Array.Empty<AllItemsEpisode>()).Count(ep => ParseDate(ep.WatchedAt) == fallbackDate)) > 1;
            var hits = 0;
            var marks = new List<(BaseItem Item, DateTime? Own, DateTime? Fallback)>();

            foreach (var season in seasons)
            {
                foreach (var episode in season.Episodes ?? Array.Empty<AllItemsEpisode>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = FindEpisode(index, series, anime && !absolute, entry, season, episode);
                    if (item == null)
                    {
                        if (series == null)
                        {
                            continue;
                        }

                        job.EpisodesNotInLibrary++;
                        _logger.LogDebug("Not in library: {Title} S{Season}E{Episode}", title, season.Number, episode.Number);
                        continue;
                    }

                    hits++;

                    // only a date Simkl really has for this episode may replace one Jellyfin already has
                    var own = ParseDate(episode.WatchedAt);
                    if (synthesized && own == fallbackDate)
                    {
                        own = null;
                    }

                    marks.Add((item, own, fallbackDate));
                }
            }

            job.EpisodesMarked += MarkPlayed(job, marks);

            if (series != null || hits > 0)
            {
                return;
            }

            job.ShowsNotInLibrary++;
            _logger.LogDebug("Not in library: show {Title} {Ids}", title, Describe(ids));
        }

        private static Episode? FindEpisode(LibraryIndex index, Series? series, bool tvdbNumbering, AllItemsEntry entry, AllItemsSeason season, AllItemsEpisode episode)
        {
            // a TVDB episode id hit counts even when the series itself did not match
            var byId = episode.Ids?.TvdbId is int tvdbId ? index.FindEpisodeByTvdbId(tvdbId) : null;
            if (byId != null || series == null)
            {
                return byId;
            }

            var (s, n) = tvdbNumbering ? TvdbCoordinates(entry, season, episode) : (season.Number, episode.Number);
            return index.FindEpisode(series.Id, s, n);
        }

        // Anime matched through TVDB/IMDB/TMDB is stored with TVDB seasons, so translate Simkl's numbers.
        // A tvdb season of 0 is deliberate (specials, anime-lists tvdbseason=0 mappings) and must never fall through
        // to mapped_tvdb_seasons, which cannot express season 0 (the server collapses '0' to [1]).
        //
        // The per-episode block is what this trusts, and deliberately so. When an episode has no mapping the server
        // fills the block with Simkl's own numbers, which is right for the episode number and says season 1 for
        // every anime cour. mapped_tvdb_seasons is NOT a second opinion to fall back on there: joinTVtoAnime.php
        // writes it from the same pass that writes ext_season/ext_episode, so the two are set together or not at
        // all, and an entry carrying a mapped season with no mapped episodes is a stale leftover from a mapping
        // that has since been removed. Preferring it would act on exactly the data that is known to be out of date.
        private static (int Season, int Number) TvdbCoordinates(AllItemsEntry entry, AllItemsSeason season, AllItemsEpisode episode)
        {
            var s = ParseInt(episode.Tvdb?.Season);
            var e = ParseInt(episode.Tvdb?.Episode);
            if (s > 0 && e > 0)
            {
                return (s.Value, e.Value);
            }

            if (s == 0 && e > 0)
            {
                return (0, e.Value);
            }

            if (season.Number == 0)
            {
                return (0, episode.Number);
            }

            var mapped = entry.MappedTvdbSeasons;
            if (mapped != null && mapped.Count == 1 && mapped[0] > 0)
            {
                return (mapped[0], episode.Number);
            }

            return (1, episode.Number);
        }

        private int MarkPlayed(Job job, List<(BaseItem Item, DateTime? Own, DateTime? Fallback)> marks)
        {
            if (marks.Count == 0)
            {
                return 0;
            }

            // The index reads no user data: loaded with the items, it is every user's rows on every item. And
            // GetUserData(user, item) reads only what the item carries, so on an item from the index it would answer
            // a blank record for everything, which the save would then write over the real one. The batch read is the
            // one that goes to the database for what the items do not carry, in one query for all of them.
            var userData = _userDataManager.GetUserDataBatch(marks.Select(m => m.Item).DistinctBy(i => i.Id).ToList(), job.User);
            return marks.Count(m => MarkPlayed(job, m.Item, userData, m.Own, m.Fallback));
        }

        private bool MarkPlayed(Job job, BaseItem item, IReadOnlyDictionary<Guid, UserItemData> userData, DateTime? own, DateTime? fallback)
        {
            if (!job.Seen.Add(item.Id))
            {
                return false;   // a multi-episode file matched twice is handled once
            }

            if (job.ChangedMeanwhile.ContainsKey(item.Id))
            {
                return false;   // changed by hand, or played, since this run started
            }

            if (!userData.TryGetValue(item.Id, out var data))
            {
                return false;   // no user data to write, so nothing to mark: the item went away mid run
            }

            if (data.Played)
            {
                job.AlreadyPlayed++;    // never touch items Jellyfin already knows as played, unless a reset asks for Simkl's date of this very item
                if (!job.Overwrite || !own.HasValue)
                {
                    return false;
                }

                job.Refreshed++;
                data.LastPlayedDate = own.Value;
                _userDataManager.SaveUserData(job.User, item, data, UserDataSaveReason.Import, CancellationToken.None);
                return false;
            }

            var watchedAt = own ?? fallback;
            data.Played = true;
            data.PlayCount = Math.Max(data.PlayCount, 1);
            if (watchedAt.HasValue)
            {
                data.LastPlayedDate = watchedAt.Value;
            }

            data.PlaybackPositionTicks = 0;
            _userDataManager.SaveUserData(job.User, item, data, UserDataSaveReason.Import, CancellationToken.None);
            return true;
        }

        private void Finish(User user, ImportState state, ImportRun run)
        {
            try
            {
                run.FinishedUtc = DateTime.UtcNow;
                if (run.Error != null)
                {
                    run.Message = run.Cancelled ? run.Error : "Import failed: " + run.Error;
                }

                _lastFinish[user.Id] = run.FinishedUtc.Value;
                state.LastImportAt = Stamp(run.FinishedUtc.Value);
                state.LastImportSummary = run.Message ?? string.Empty;
                _store.Save(user.Id, user.Username, state);
            }
            finally
            {
                run.Running = false;
            }
        }

        // newest watch-related activity per type; a cursor is the value seen when a run started
        private static Dictionary<string, string?> Cursors(ActivitiesResponse? activities)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                { "shows", Latest(activities?.TvShows) },
                { "anime", Latest(activities?.Anime) },
                { "movies", Latest(activities?.Movies) }
            };
        }

        private static string? Latest(ActivitiesBucket? bucket)
        {
            if (bucket == null)
            {
                return null;
            }

            string? latest = null;
            foreach (var value in new[] { bucket.Watching, bucket.Hold, bucket.Completed, bucket.Dropped, bucket.NotInteresting })
            {
                // same ISO format, so ordinal order is time order
                if (value != null && (latest == null || string.CompareOrdinal(value, latest) > 0))
                {
                    latest = value;
                }
            }

            return latest;
        }

        private static List<string> ChangedTypes(ImportState state, Dictionary<string, string?> cursors)
        {
            var changed = new List<string>();
            foreach (var type in Types)
            {
                var latest = cursors[type];
                var since = Cursor(state, type);
                if (latest != null && (string.IsNullOrEmpty(since) || string.CompareOrdinal(latest, since) > 0))
                {
                    changed.Add(type);
                }
            }

            return changed;
        }

        private static string Cursor(ImportState state, string type)
        {
            return string.Equals(type, "shows", StringComparison.Ordinal) ? state.CursorShows
                : string.Equals(type, "anime", StringComparison.Ordinal) ? state.CursorAnime
                : state.CursorMovies;
        }

        private static void SetCursor(ImportState state, string type, string value)
        {
            if (string.Equals(type, "shows", StringComparison.Ordinal))
            {
                state.CursorShows = value;
            }
            else if (string.Equals(type, "anime", StringComparison.Ordinal))
            {
                state.CursorAnime = value;
            }
            else
            {
                state.CursorMovies = value;
            }
        }

        private static string Normalize(string? status)
        {
            return (string.Equals(status, DroppedSegment, StringComparison.Ordinal) ? "dropped" : status) ?? string.Empty;
        }

        private static int? ParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
        }

        private static string Summary(Job job, bool nothingChanged)
        {
            if (nothingChanged)
            {
                return "Nothing new on Simkl since the last import.";
            }

            var refreshed = job.Overwrite ? FormattableString.Invariant($" ({job.Refreshed} watch dates refreshed from Simkl)") : string.Empty;
            return FormattableString.Invariant($"Marked {job.MoviesMarked} movies and {job.EpisodesMarked} episodes as played. ")
                + FormattableString.Invariant($"{job.AlreadyPlayed} already played{refreshed}. ")
                + FormattableString.Invariant($"Not in your Jellyfin library: {job.MoviesNotInLibrary} movies, {job.ShowsNotInLibrary} shows, {job.EpisodesNotInLibrary} episodes.");
        }

        private static string Describe(SimklIds? ids)
        {
            return ids == null
                ? "(no ids)"
                : FormattableString.Invariant($"simkl={ids.Simkl} imdb={ids.Imdb} tmdb={ids.Tmdb} tvdb={ids.Tvdb} mal={ids.Mal} anidb={ids.Anidb} anilist={ids.Anilist}");
        }

        // any run within the last CooldownMinutes blocks another; the message names both times
        private string? CooldownMessage(Guid userId)
        {
            if (!_lastFinish.TryGetValue(userId, out var last))
            {
                return null;
            }

            var elapsed = DateTime.UtcNow - last;
            if (elapsed.TotalMinutes >= CooldownMinutes)
            {
                return null;
            }

            var ago = (int)elapsed.TotalMinutes;
            return "An import ran " + (ago == 0 ? "less than a minute" : Minutes(ago)) + " ago. Wait " + Minutes(CooldownMinutes - ago) + " before running another.";
        }

        private static string Minutes(int count)
        {
            return FormattableString.Invariant($"{count}") + (count == 1 ? " minute" : " minutes");
        }

        private static ImportStatus ToStatus(ImportRun? run, ImportState state)
        {
            var interrupted = run == null && string.Equals(state.LastImportSummary, InterruptedSummary, StringComparison.Ordinal);
            var finished = run != null && !run.Running && run.FinishedUtc.HasValue;   // in-memory result wins while the process lives
            return new ImportStatus
            {
                Running = run?.Running ?? false,
                Message = run?.Message ?? Stored(state.LastImportSummary),
                Error = run?.Error ?? (interrupted ? InterruptedSummary : null),
                LastImportAt = finished ? Stamp(run!.FinishedUtc!.Value) : Stored(state.LastImportAt),
                LastAutoRunAt = Stored(state.LastAutoRunAt)
            };
        }

        private ImportStatus Refused(Guid userId, string error)
        {
            var status = ToStatus(null, _store.Load(userId));
            status.Error = error;
            return status;
        }

        private static string? Stored(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private sealed class ImportRun
        {
            public bool Running { get; set; }

            // a full import from TryStart; a running check refuses one instead of being joined
            public bool Manual { get; set; }

            // started by the task or by switching the toggle on; only these stamp lastAutoRunAt
            public bool Scheduled { get; set; }

            public bool Cancelled { get; set; }

            // forget cursors and re-apply Simkl's watch state even to items already played
            public bool Reset { get; set; }

            public string? Message { get; set; }

            public string? Error { get; set; }

            public DateTime StartedUtc { get; set; }

            public DateTime? FinishedUtc { get; set; }
        }

        private sealed class Job
        {
            public Job(User user, bool overwrite)
            {
                User = user;
                Overwrite = overwrite;
            }

            public User User { get; }

            public bool Overwrite { get; }

            // a multi-episode file matched twice is handled once
            public HashSet<Guid> Seen { get; } = new HashSet<Guid>();

            // items whose play state changed outside this run while it was going
            public ConcurrentDictionary<Guid, byte> ChangedMeanwhile { get; } = new ConcurrentDictionary<Guid, byte>();

            public int MoviesMarked { get; set; }

            public int EpisodesMarked { get; set; }

            public int AlreadyPlayed { get; set; }

            public int Refreshed { get; set; }

            public int MoviesNotInLibrary { get; set; }

            public int ShowsNotInLibrary { get; set; }

            public int EpisodesNotInLibrary { get; set; }

            public void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
            {
                if (e.UserId != User.Id || e.SaveReason == UserDataSaveReason.Import || e.Item == null)
                {
                    return;
                }

                ChangedMeanwhile[e.Item.Id] = 0;
            }
        }
    }
}
