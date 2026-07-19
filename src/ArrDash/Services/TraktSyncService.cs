using System.Text.Json;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public sealed class TraktSyncService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TraktClient trakt,
    TraktAccountService accounts,
    ActivityAnalyticsService activityAnalytics,
    WatchStatsService watchStats,
    LayoutPreferencesService prefs,
    ILogger<TraktSyncService> logger) : BackgroundService
{
    private readonly object _statusLock = new();
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private string _statusMessage = "Idle";

    public string StatusMessage
    {
        get { lock (_statusLock) return _statusMessage; }
    }

    public bool IsBusy => _syncLock.CurrentCount == 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var list = await accounts.ListAccountsAsync(stoppingToken);
                foreach (var account in list.Where(a => a.ImportToWarehouse && a.LastPreviewAtUtc is not null))
                    await SyncAccountAsync(account.Id, previewOnly: false, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Trakt background sync failed");
                SetStatus($"Sync failed: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
        }
    }

    public Task<TraktSyncPreview> PreviewAsync(string accountId, CancellationToken ct) =>
        SyncAccountAsync(accountId, previewOnly: true, ct);

    public Task<TraktSyncPreview> SyncNowAsync(string accountId, CancellationToken ct) =>
        SyncAccountAsync(accountId, previewOnly: false, ct);

    /// <summary>Fire-and-forget Sync Now so the Settings UI does not block for large histories.</summary>
    public bool TryStartSyncInBackground(string accountId, out string message)
    {
        if (!_syncLock.Wait(0))
        {
            message = StatusMessage;
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SyncAccountAsync(accountId, previewOnly: false, CancellationToken.None, alreadyLocked: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background Trakt sync failed for {AccountId}", accountId);
                SetStatus($"Sync failed: {ex.Message}");
            }
            finally
            {
                _syncLock.Release();
            }
        });

        message = "Sync started in background…";
        return true;
    }

    private async Task<TraktSyncPreview> SyncAccountAsync(
        string accountId,
        bool previewOnly,
        CancellationToken ct,
        bool alreadyLocked = false)
    {
        if (!alreadyLocked)
            await _syncLock.WaitAsync(ct);

        try
        {
            SetStatus(previewOnly ? "Fetching Trakt history…" : "Fetching Trakt history…");
            var (accessToken, account) = await accounts.GetValidAccessTokenAsync(accountId, ct);

            SetStatus(previewOnly ? "Downloading movies…" : "Downloading movies…");
            var movies = account.SyncMovies
                ? await trakt.GetHistoryAsync(accessToken, "movies", account.HistoryStartUtc, ct)
                : [];
            SetStatus($"Downloading episodes… ({movies.Count:N0} movies)");
            var episodes = account.SyncEpisodes
                ? await trakt.GetHistoryAsync(accessToken, "episodes", account.HistoryStartUtc, ct)
                : [];

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Links whose play event was deleted (e.g. retention prune) would otherwise
            // block those history items from ever re-importing (#36).
            await db.TraktHistoryLinks
                .Where(l => l.AccountId == accountId
                            && l.PlayEventId != null
                            && !db.PlayEvents.Any(p => p.Id == l.PlayEventId))
                .ExecuteDeleteAsync(ct);

            var linkedIds = await db.TraktHistoryLinks.AsNoTracking()
                .Where(l => l.AccountId == accountId)
                .Select(l => l.TraktHistoryId)
                .ToListAsync(ct);
            var linkedSet = linkedIds.ToHashSet();

            var wouldImport = 0;
            var wouldLink = 0;
            var unmatched = 0;
            var samples = new List<string>();
            var pendingEntities = new List<(PlayEventEntity Entity, long TraktHistoryId)>();

            if (account.ImportToWarehouse)
            {
                var history = movies.Concat(episodes).ToList();
                var i = 0;
                foreach (var item in history)
                {
                    i++;
                    if (i % 500 == 0)
                        SetStatus($"{(previewOnly ? "Previewing" : "Preparing")} {i:N0}/{history.Count:N0}…");

                    var mapped = MapHistoryItem(item, account.CanonicalUserName);
                    if (mapped is null)
                    {
                        unmatched++;
                        continue;
                    }

                    if (linkedSet.Contains(item.Id))
                    {
                        wouldLink++;
                        continue;
                    }

                    wouldImport++;
                    if (samples.Count < 8)
                        samples.Add(mapped.Title);

                    if (!previewOnly)
                        pendingEntities.Add((PlayEventImportHelper.MapEntity(ToImported(mapped, item.Id)), item.Id));
                }

                if (!previewOnly && pendingEntities.Count > 0)
                {
                    const int batchSize = 250;
                    for (var offset = 0; offset < pendingEntities.Count; offset += batchSize)
                    {
                        var batch = pendingEntities.Skip(offset).Take(batchSize).ToList();
                        SetStatus($"Importing {Math.Min(offset + batch.Count, pendingEntities.Count):N0}/{pendingEntities.Count:N0}…");

                        foreach (var (entity, _) in batch)
                            db.PlayEvents.Add(entity);

                        await db.SaveChangesAsync(ct);

                        foreach (var (entity, traktHistoryId) in batch)
                        {
                            db.TraktHistoryLinks.Add(new TraktHistoryLinkEntity
                            {
                                AccountId = accountId,
                                PlayEventId = entity.Id,
                                TraktHistoryId = traktHistoryId,
                                Direction = "pull",
                                CanonicalMediaKey = entity.CanonicalMediaKey ?? ""
                            });
                            linkedSet.Add(traktHistoryId);
                        }

                        await db.SaveChangesAsync(ct);
                    }
                }
            }

            var wouldPush = 0;
            if (account.PushToTrakt && !previewOnly)
                wouldPush = await PushLocalPlaysAsync(db, account, accessToken, ct);
            else if (account.PushToTrakt)
                wouldPush = await CountPushCandidatesAsync(db, account, ct);

            if (!previewOnly)
            {
                var tracked = await db.TraktAccounts.FindAsync([accountId], ct);
                if (tracked is not null)
                {
                    tracked.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                    tracked.LastError = null;
                    tracked.LastPreviewJson = JsonSerializer.Serialize(new
                    {
                        wouldImport,
                        wouldLink,
                        wouldPush,
                        unmatched,
                        imported = pendingEntities.Count
                    });
                }

                await db.SaveChangesAsync(ct);
                activityAnalytics.InvalidateCache();
                watchStats.InvalidateCache();
            }
            else
            {
                var tracked = await db.TraktAccounts.FindAsync([accountId], ct);
                if (tracked is not null)
                {
                    tracked.LastPreviewAtUtc = DateTimeOffset.UtcNow;
                    tracked.LastPreviewJson = JsonSerializer.Serialize(new
                    {
                        wouldImport,
                        wouldLink,
                        wouldPush,
                        unmatched
                    });
                    await db.SaveChangesAsync(ct);
                }
            }

            var note = account.MarkPlexWatched || account.MarkEmbyWatched || account.MarkJellyfinWatched
                ? "Server watched-state writes are configured but preview-gated; enable after reviewing matches. Additive only."
                : null;

            var preview = new TraktSyncPreview(
                accountId,
                movies.Count,
                episodes.Count,
                wouldImport,
                wouldLink,
                wouldPush,
                unmatched,
                samples,
                note);

            SetStatus(previewOnly
                ? $"Preview: import {wouldImport:N0}, already linked {wouldLink:N0}"
                : $"Synced: imported {pendingEntities.Count:N0}, already linked {wouldLink:N0}");
            return preview;
        }
        finally
        {
            if (!alreadyLocked)
                _syncLock.Release();
        }
    }

    private async Task<int> PushLocalPlaysAsync(
        ArrDashDbContext db,
        TraktAccountEntity account,
        string accessToken,
        CancellationToken ct)
    {
        var mappedNames = ParseMappedNames(account).ToList();
        var excludedLibraries = prefs.Current.WatchStatsExcludedLibraries;
        // Plays with an unknown library are never pushed — it may belong to an excluded
        // library that enrichment hasn't identified yet (#38).
        var candidates = await db.PlayEvents.AsNoTracking()
            .Where(e => e.Source != WatchStatsSources.Trakt
                        && e.WasCompleted
                        && (e.MediaType == "movie" || e.MediaType == "episode")
                        && (e.UserDisplayName == account.CanonicalUserName
                            || mappedNames.Contains(e.UserDisplayName))
                        && (e.TraktId != null
                            || (e.ImdbId != null && e.ImdbId != "")
                            || e.TmdbId != null
                            || e.TvdbId != null))
            .OrderByDescending(e => e.PlayedAtUtc)
            .Take(5_000)
            .ToListAsync(ct);

        var pushedPlayIds = await db.TraktHistoryLinks.AsNoTracking()
            .Where(l => l.AccountId == account.Id && l.Direction == "push" && l.PlayEventId != null)
            .Select(l => l.PlayEventId!.Value)
            .ToListAsync(ct);
        var pushedSet = pushedPlayIds.ToHashSet();

        var movies = new List<object>();
        var episodes = new List<object>();
        var shows = new Dictionary<string, ShowPushBucket>(StringComparer.OrdinalIgnoreCase);
        var linked = 0;

        foreach (var ev in candidates)
        {
            if (pushedSet.Contains(ev.Id))
                continue;

            // Prefer skipping unknown libraries only when we know an exclusion list applies.
            if (!string.IsNullOrWhiteSpace(ev.LibraryExternalId)
                && WatchStatsLibraryFilter.IsExcluded(excludedLibraries, ev.Source, ev.LibraryExternalId))
                continue;

            var queued = false;
            if (ev.MediaType == "movie" && account.SyncMovies)
            {
                var ids = BuildIds(ev);
                if (ids is null)
                    continue;
                movies.Add(new { watched_at = FormatWatchedAt(ev.PlayedAtUtc), ids });
                queued = true;
            }
            else if (ev.MediaType == "episode" && account.SyncEpisodes)
            {
                if (ev.TraktId is int epTrakt)
                {
                    episodes.Add(new { watched_at = FormatWatchedAt(ev.PlayedAtUtc), ids = new { trakt = epTrakt } });
                    queued = true;
                }
                else if (ev.SeasonNumber is int sn && ev.EpisodeNumber is int en)
                {
                    // Tracearr usually stores the series id — treat provider ids as show-level.
                    queued = AddShowEpisode(shows, ev, sn, en);
                }
            }

            if (!queued)
                continue;

            db.TraktHistoryLinks.Add(new TraktHistoryLinkEntity
            {
                AccountId = account.Id,
                PlayEventId = ev.Id,
                // Pull links store the real Trakt history id; push links use a negative
                // play-event id so (AccountId, TraktHistoryId) stays unique.
                TraktHistoryId = -ev.Id,
                Direction = "push",
                CanonicalMediaKey = ev.CanonicalMediaKey ?? ""
            });
            linked++;
        }

        var showPayload = shows.Values
            .Select(s => new
            {
                ids = s.Ids,
                seasons = s.Seasons.Select(season => new
                {
                    number = season.Key,
                    episodes = season.Value.Select(ep => new
                    {
                        number = ep.Episode,
                        watched_at = FormatWatchedAt(ep.WatchedAt)
                    }).ToList()
                }).ToList()
            })
            .ToList();

        if (movies.Count == 0 && episodes.Count == 0 && showPayload.Count == 0)
            return 0;

        await trakt.AddToHistoryAsync(accessToken, new { movies, episodes, shows = showPayload }, ct);
        await db.SaveChangesAsync(ct);
        return linked;
    }

    private static bool AddShowEpisode(
        Dictionary<string, ShowPushBucket> shows,
        PlayEventEntity ev,
        int season,
        int episode)
    {
        var ids = BuildIds(ev);
        if (ids is null)
            return false;

        var key = $"{ev.ImdbId}|{ev.TmdbId}|{ev.TvdbId}|{ev.TraktId}";
        if (!shows.TryGetValue(key, out var bucket))
        {
            bucket = new ShowPushBucket(ids);
            shows[key] = bucket;
        }

        if (!bucket.Seasons.TryGetValue(season, out var eps))
        {
            eps = [];
            bucket.Seasons[season] = eps;
        }

        eps.Add((episode, ev.PlayedAtUtc));
        return true;
    }

    private static object? BuildIds(PlayEventEntity ev)
    {
        if (ev.TraktId is int trakt)
            return new { trakt };
        if (!string.IsNullOrWhiteSpace(ev.ImdbId))
            return new { imdb = ev.ImdbId };
        if (ev.TmdbId is int tmdb)
            return new { tmdb };
        if (ev.TvdbId is int tvdb)
            return new { tvdb };
        return null;
    }

    private static string FormatWatchedAt(DateTime utc) =>
        utc.ToString("yyyy-MM-ddTHH:mm:ssZ");

    private sealed class ShowPushBucket(object ids)
    {
        public object Ids { get; } = ids;
        public Dictionary<int, List<(int Episode, DateTime WatchedAt)>> Seasons { get; } = new();
    }

    private async Task<int> CountPushCandidatesAsync(
        ArrDashDbContext db,
        TraktAccountEntity account,
        CancellationToken ct)
    {
        var mappedNames = ParseMappedNames(account).ToList();
        var excludedLibraries = prefs.Current.WatchStatsExcludedLibraries;
        var rows = await db.PlayEvents.AsNoTracking()
            .Where(e => e.Source != WatchStatsSources.Trakt
                        && e.WasCompleted
                        && (e.MediaType == "movie" || e.MediaType == "episode")
                        && (e.UserDisplayName == account.CanonicalUserName || mappedNames.Contains(e.UserDisplayName))
                        && (e.TraktId != null
                            || (e.ImdbId != null && e.ImdbId != "")
                            || e.TmdbId != null
                            || e.TvdbId != null)
                        && (e.MediaType != "episode"
                            || e.TraktId != null
                            || (e.SeasonNumber != null && e.EpisodeNumber != null)))
            .Select(e => new { e.Source, e.LibraryExternalId })
            .ToListAsync(ct);

        return rows.Count(r =>
            string.IsNullOrWhiteSpace(r.LibraryExternalId)
            || !WatchStatsLibraryFilter.IsExcluded(excludedLibraries, r.Source, r.LibraryExternalId));
    }

    private HashSet<string> ParseMappedNames(TraktAccountEntity account)
    {
        try
        {
            var users = JsonSerializer.Deserialize<List<TraktMappedUser>>(account.MappedUsersJson) ?? [];
            return users.Select(u => u.UserName).Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ParseMappedNames: malformed MappedUsersJson for account {AccountId}", account.Id);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static PlayEventEntity? MapHistoryItem(TraktHistoryItem item, string user)
    {
        if (item.Movie is not null)
        {
            var m = item.Movie;
            var ids = m.Ids;
            var runtime = (m.Runtime ?? 0) * 60;
            var key = CanonicalMediaKeyBuilder.Build(
                "movie", ids?.Imdb, ids?.Tmdb, ids?.Tvdb, ids?.Trakt, m.Title, m.Year);
            return new PlayEventEntity
            {
                Source = WatchStatsSources.Trakt,
                ExternalPlayId = $"trakt:{item.Id}",
                UserDisplayName = user,
                Title = m.Title ?? "Unknown",
                MediaType = "movie",
                PlayedAtUtc = item.WatchedAt.UtcDateTime,
                DurationSeconds = runtime,
                Origin = WatchStatsSources.Trakt,
                Year = m.Year,
                ImdbId = ids?.Imdb,
                TmdbId = ids?.Tmdb,
                TvdbId = ids?.Tvdb,
                TraktId = ids?.Trakt,
                WasCompleted = true,
                DurationIsEstimated = true,
                CanonicalMediaKey = key,
                GenresJson = "[]"
            };
        }

        if (item.Episode is not null && item.Show is not null)
        {
            var ep = item.Episode;
            var show = item.Show;
            var ids = ep.Ids ?? show.Ids;
            var runtime = (ep.Runtime ?? 0) * 60;
            var key = CanonicalMediaKeyBuilder.Build(
                "episode", ids?.Imdb, ids?.Tmdb, ids?.Tvdb, ids?.Trakt ?? show.Ids?.Trakt,
                show.Title, show.Year, ep.Season, ep.Number);
            return new PlayEventEntity
            {
                Source = WatchStatsSources.Trakt,
                ExternalPlayId = $"trakt:{item.Id}",
                UserDisplayName = user,
                Title = show.Title ?? "Unknown",
                SeriesTitle = show.Title,
                ItemTitle = ep.Title,
                MediaType = "episode",
                PlayedAtUtc = item.WatchedAt.UtcDateTime,
                DurationSeconds = runtime,
                Origin = WatchStatsSources.Trakt,
                Year = show.Year,
                SeasonNumber = ep.Season,
                EpisodeNumber = ep.Number,
                ImdbId = ids?.Imdb,
                TmdbId = ids?.Tmdb,
                TvdbId = ids?.Tvdb,
                TraktId = ep.Ids?.Trakt ?? show.Ids?.Trakt,
                WasCompleted = true,
                DurationIsEstimated = true,
                CanonicalMediaKey = key,
                GenresJson = "[]"
            };
        }

        return null;
    }

    private static ImportedPlayEvent ToImported(PlayEventEntity e, long traktHistoryId) =>
        new(
            WatchStatsSources.Trakt,
            $"trakt:{traktHistoryId}",
            e.UserDisplayName,
            null,
            e.Title,
            e.SeriesTitle,
            e.MediaType,
            [],
            null,
            "Trakt",
            new DateTimeOffset(e.PlayedAtUtc, TimeSpan.Zero),
            e.DurationSeconds,
            null,
            null,
            ItemTitle: e.ItemTitle,
            Origin: WatchStatsSources.Trakt,
            Year: e.Year,
            SeasonNumber: e.SeasonNumber,
            EpisodeNumber: e.EpisodeNumber,
            ImdbId: e.ImdbId,
            TmdbId: e.TmdbId,
            TvdbId: e.TvdbId,
            TraktId: e.TraktId,
            WasCompleted: true,
            DurationIsEstimated: true);

    private void SetStatus(string message)
    {
        lock (_statusLock)
            _statusMessage = message;
    }
}
