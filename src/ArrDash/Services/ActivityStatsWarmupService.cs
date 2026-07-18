using ArrDash.Models;

namespace ArrDash.Services;

public sealed class ActivityStatsWarmupService(
    ActivityAnalyticsService activity,
    ActivitySourceAvailability availability,
    LayoutPreferencesService prefs,
    ILogger<ActivityStatsWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan WarmupInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(8);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WarmAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Activity stats warmup failed");
            }

            await Task.Delay(WarmupInterval, stoppingToken);
        }
    }

    private async Task WarmAsync(CancellationToken ct)
    {
        var filters = await availability.GetAvailableFiltersAsync(ct);
        if (filters.Count == 0)
            return;

        var ranges = new[]
        {
            new WatchStatsRange(WatchStatsPeriod.Days7),
            new WatchStatsRange(WatchStatsPeriod.Days30),
            WatchStatsRange.Year,
            new WatchStatsRange(WatchStatsPeriod.All)
        };

        foreach (var filter in filters)
        {
            foreach (var range in ranges)
            {
                var libraryFp = WatchStatsLibraryFilter.PreferenceFingerprint(prefs.Current.WatchStatsExcludedLibraries);
                var key = ActivitySnapshotFileCache.BuildKey(range, filter, libraryFp);
                var cached = await activity.TryGetCachedAsync(key, ct);
                if (cached is not null && cached.Age < StaleAfter)
                    continue;

                await activity.GetAsync(range, filter, force: true, ct);
            }
        }
    }
}
