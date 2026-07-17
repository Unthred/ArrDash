using ArrDash.Models;

namespace ArrDash.Services;

public sealed class ActivityAnalyticsService(
    WatchStatsService watchStats,
    WatchStatsRepository repository,
    ActivitySourceAvailability availability,
    ActivitySnapshotFileCache fileCache,
    LayoutPreferencesService prefs)
{
    private readonly object _cacheLock = new();
    private ActivityAnalyticsSnapshot? _cached;
    private (WatchStatsRange Range, WatchStatsSourceFilter Filter) _cacheKey;
    private DateTimeOffset _cachedAt;
    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FileCacheServeTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FileCacheRefreshAfter = TimeSpan.FromMinutes(5);
    private readonly HashSet<string> _refreshInFlight = [];

    private readonly object _drilldownLock = new();
    private readonly Dictionary<string, (ActivityDrilldownSnapshot Snapshot, DateTimeOffset CachedAt)> _drilldownCache = new();
    private static readonly TimeSpan DrilldownCacheTtl = TimeSpan.FromMinutes(5);

    public Task<ActivitySnapshotFileCache.CachedActivitySnapshot?> TryGetCachedAsync(
        string cacheKey,
        CancellationToken ct = default) =>
        fileCache.TryGetAsync(cacheKey, ct);

    public async Task<ActivityAnalyticsSnapshot> GetAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        bool force = false,
        CancellationToken ct = default)
    {
        if (!range.IsQueryable)
            range = WatchStatsRange.Year;

        var libraryFp = WatchStatsLibraryFilter.PreferenceFingerprint(prefs.Current.WatchStatsIncludedLibraries);
        var fileKey = ActivitySnapshotFileCache.BuildKey(range, filter, libraryFp);

        if (!force)
        {
            lock (_cacheLock)
            {
                if (_cached is not null
                    && WatchStatsPeriodHelper.RangesEqual(_cacheKey.Range, range)
                    && _cacheKey.Filter == filter
                    && DateTimeOffset.UtcNow - _cachedAt < MemoryCacheTtl)
                    return _cached;
            }

            var disk = await fileCache.TryGetAsync(fileKey, ct);
            if (disk is not null && disk.Age < FileCacheServeTtl)
            {
                lock (_cacheLock)
                {
                    _cached = disk.Snapshot;
                    _cacheKey = (range, filter);
                    _cachedAt = DateTimeOffset.UtcNow;
                }

                if (disk.Age > FileCacheRefreshAfter)
                    QueueBackgroundRefresh(range, filter, fileKey);

                return disk.Snapshot;
            }
        }

        var snapshot = await BuildSnapshotAsync(range, filter, ct);

        lock (_cacheLock)
        {
            _cached = snapshot;
            _cacheKey = (range, filter);
            _cachedAt = DateTimeOffset.UtcNow;
        }

        await fileCache.SaveAsync(fileKey, snapshot, ct);
        return snapshot;
    }

    private async Task<ActivityAnalyticsSnapshot> BuildSnapshotAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        CancellationToken ct)
    {
        var configured = await availability.GetConfiguredSourcesAsync(ct);
        var enabled = configured.Where(s => prefs.IsServiceEnabled(s)).ToList();
        var limit = Math.Clamp(prefs.Current.WatchStatsTopLimit, 3, 25);
        var warehouseCount = await repository.CountEventsAsync(ct);

        var analytics = await repository.BuildAnalyticsAsync(
            range, filter, enabled, prefs.Current.UserAliases, limit, ct);
        var watchSnapshot = warehouseCount > 0
            ? null
            : await watchStats.GetAsync(range, filter, force: true, ct);

        var leaderboards = ResolveLeaderboards(watchSnapshot, filter, analytics, warehouseCount);
        var summary = warehouseCount > 0 ? analytics.Summary : leaderboards?.Summary ?? analytics.Summary;
        var warehouseNote = await DescribeWarehouseStatusAsync(enabled, warehouseCount, ct);

        return new ActivityAnalyticsSnapshot(
            range.Period,
            filter,
            summary,
            ActiveStreams: 0,
            summary.UniqueUsers,
            summary.TotalPlays,
            analytics.PlaysOverTime,
            analytics.MediaMix,
            analytics.ByDayOfWeek,
            analytics.ByHourOfDay,
            analytics.Platforms,
            analytics.QualityBundle.Quality,
            analytics.QualityBundle.Stats,
            analytics.ConcurrentStreams,
            TodayStats: range.Period == WatchStatsPeriod.Today ? BuildTodayStats(analytics) : null,
            leaderboards,
            [warehouseNote],
            DateTimeOffset.UtcNow);
    }

    private static ActivityTodayStats? BuildTodayStats(ActivityAnalyticsBundle analytics)
    {
        if (analytics.Summary.TotalPlays == 0)
            return null;

        return new ActivityTodayStats(
            analytics.Summary.TotalPlays,
            analytics.Summary.TotalHours,
            analytics.Summary.UniqueUsers,
            0);
    }

    private static WatchStatsSourceSnapshot? ResolveLeaderboards(
        WatchStatsSnapshot? watchSnapshot,
        WatchStatsSourceFilter filter,
        ActivityAnalyticsBundle analytics,
        long warehouseCount)
    {
        if (warehouseCount > 0)
        {
            if (filter == WatchStatsSourceFilter.Combined)
                return analytics.Leaderboard with { Key = "combined", Label = "Combined" };

            var key = MapFilterKey(filter);
            return analytics.Leaderboard with { Key = key, Label = WatchStatsSources.Label(key) };
        }

        if (watchSnapshot is null)
            return null;

        if (filter == WatchStatsSourceFilter.Combined)
            return watchSnapshot.Combined;

        var filterKey = MapFilterKey(filter);
        return watchSnapshot.Sources.FirstOrDefault(s =>
                   string.Equals(s.Key, filterKey, StringComparison.OrdinalIgnoreCase))
               ?? watchSnapshot.Sources.FirstOrDefault(s =>
                   string.Equals(s.Label, WatchStatsSources.Label(filterKey), StringComparison.OrdinalIgnoreCase));
    }

    private static Task<ActivityDataSourceStatus> DescribeWarehouseStatusAsync(
        IReadOnlyList<string> enabled,
        long total,
        CancellationToken ct)
    {
        var label = total > 0
            ? $"Play history warehouse ({total:N0} events)"
            : "Play history warehouse (empty — enable sync in Settings)";

        return Task.FromResult(new ActivityDataSourceStatus(
            "warehouse",
            label,
            total > 0,
            total == 0 ? "No synced play events yet" : null));
    }

    private void QueueBackgroundRefresh(WatchStatsRange range, WatchStatsSourceFilter filter, string fileKey)
    {
        lock (_refreshInFlight)
        {
            if (!_refreshInFlight.Add(fileKey))
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var snapshot = await BuildSnapshotAsync(range, filter, CancellationToken.None);
                lock (_cacheLock)
                {
                    _cached = snapshot;
                    _cacheKey = (range, filter);
                    _cachedAt = DateTimeOffset.UtcNow;
                }

                await fileCache.SaveAsync(fileKey, snapshot, CancellationToken.None);
            }
            catch
            {
                // Background refresh is best-effort.
            }
            finally
            {
                lock (_refreshInFlight)
                    _refreshInFlight.Remove(fileKey);
            }
        });
    }

    public async Task<ActivityDrilldownSnapshot> GetDrilldownAsync(
        ActivityDrilldownRequest request,
        CancellationToken ct = default)
    {
        var cacheKey = $"{request.Kind}|{request.Key}|{request.Title}|{request.Source}|{request.Period}|{request.CustomStartUtc:yyyy-MM-dd}|{request.CustomEndUtc:yyyy-MM-dd}";
        lock (_drilldownLock)
        {
            if (_drilldownCache.TryGetValue(cacheKey, out var cached)
                && DateTimeOffset.UtcNow - cached.CachedAt < DrilldownCacheTtl)
                return cached.Snapshot;
        }

        var range = new WatchStatsRange(request.Period, request.CustomStartUtc, request.CustomEndUtc);
        var dbEvents = await repository.QueryEventsAsync(range, request.Source, ct);
        dbEvents = FilterEventsForRequest(request, dbEvents);

        var snapshot = PlayEventAnalyticsService.BuildDrilldown(request, dbEvents);

        lock (_drilldownLock)
            _drilldownCache[cacheKey] = (snapshot, DateTimeOffset.UtcNow);

        return snapshot;
    }

    private static IReadOnlyList<Data.Entities.PlayEventEntity> FilterEventsForRequest(
        ActivityDrilldownRequest request,
        IReadOnlyList<Data.Entities.PlayEventEntity> events)
    {
        if (request.Kind != ActivityDrilldownKind.Library)
            return events;

        var needle = !string.IsNullOrWhiteSpace(request.Key) ? request.Key : request.Title;
        if (string.IsNullOrWhiteSpace(needle))
            return events;

        return events
            .Where(e => string.Equals(e.LibraryExternalId, needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.LibraryName, needle, StringComparison.OrdinalIgnoreCase)
                || (e.LibraryName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
            _cached = null;
        lock (_drilldownLock)
            _drilldownCache.Clear();
    }

    private static string MapFilterKey(WatchStatsSourceFilter filter) => filter switch
    {
        WatchStatsSourceFilter.Plex => WatchStatsSources.Plex,
        WatchStatsSourceFilter.Emby => WatchStatsSources.Emby,
        WatchStatsSourceFilter.Jellyfin => WatchStatsSources.Jellyfin,
        _ => "combined"
    };
}
