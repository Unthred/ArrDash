using ArrDash.Models;

namespace ArrDash.Services;

public sealed class WatchStatsService(
    LiveWatchStatsProvider liveProvider,
    WatchHistorySyncService syncService,
    WatchStatsSnapshotFileCache fileCache)
{
    private readonly object _cacheLock = new();
    private WatchStatsSnapshot? _cached;
    private (WatchStatsRange Range, WatchStatsSourceFilter Filter) _cacheKey;
    private DateTimeOffset _cachedAt;

    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FileCacheServeTtl = TimeSpan.FromMinutes(30);

    public async Task<WatchStatsSnapshot> GetAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        bool force = false,
        CancellationToken ct = default,
        int? topLimit = null)
    {
        if (!range.IsQueryable)
            range = WatchStatsRange.Year;

        var fileKey = WatchStatsSnapshotFileCache.BuildKey(range, filter);
        var useFileCache = topLimit is null;

        if (!force)
        {
            lock (_cacheLock)
            {
                if (_cached is not null
                    && WatchStatsPeriodHelper.RangesEqual(_cacheKey.Range, range)
                    && _cacheKey.Filter == filter
                    && topLimit is null
                    && DateTimeOffset.UtcNow - _cachedAt < MemoryCacheTtl)
                    return _cached;
            }

            if (useFileCache)
            {
                var disk = await fileCache.TryGetAsync(fileKey, ct);
                if (disk is not null && disk.Age < FileCacheServeTtl)
                {
                    var withSync = disk.Snapshot with { SyncStatus = syncService.CurrentStatus };
                    lock (_cacheLock)
                    {
                        _cached = withSync;
                        _cacheKey = (range, filter);
                        _cachedAt = DateTimeOffset.UtcNow;
                    }

                    return withSync;
                }
            }
        }

        var snapshot = await liveProvider.GetAsync(range, filter, ct, topLimit) with
        {
            SyncStatus = syncService.CurrentStatus
        };

        if (topLimit is null)
        {
            lock (_cacheLock)
            {
                _cached = snapshot;
                _cacheKey = (range, filter);
                _cachedAt = DateTimeOffset.UtcNow;
            }

            await fileCache.SaveAsync(fileKey, snapshot, ct);
        }

        return snapshot;
    }

    public void InvalidateCache()
    {
        lock (_cacheLock)
            _cached = null;
    }
}
