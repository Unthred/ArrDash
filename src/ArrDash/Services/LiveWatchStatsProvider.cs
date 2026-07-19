using ArrDash.Configuration;
using ArrDash.Models;
using Microsoft.Extensions.Options;

namespace ArrDash.Services;

/// <summary>
/// Watch stats from the local PlayEvents warehouse only (no live Tautulli/Tracearr reads).
/// </summary>
public sealed class LiveWatchStatsProvider(
    WatchStatsRepository repository,
    ActivitySourceAvailability availability,
    LayoutPreferencesService prefs,
    IOptions<WatchStatsOptions> watchStatsOptions)
{
    private readonly WatchStatsOptions _options = watchStatsOptions.Value;

    public async Task<WatchStatsSnapshot> GetAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        CancellationToken ct = default,
        int? topLimit = null)
    {
        var limit = topLimit ?? Math.Clamp(prefs.Current.WatchStatsTopLimit, 3, 25);
        if (limit <= 0)
            limit = Math.Clamp(_options.TopListLimit, 3, 25);
        limit = Math.Clamp(limit, 3, 50);

        var aliases = prefs.Current.UserAliases;
        var enabledSources = await GetEnabledSourcesAsync(ct);
        var sourceSnapshots = new List<WatchStatsSourceSnapshot>();

        foreach (var source in enabledSources)
        {
            if (!IsSourceVisible(source))
                continue;

            var selectedKeys = WatchStatsSourceFilters.ToSourceKeys(filter);
            if (selectedKeys.Count > 0
                && !selectedKeys.Contains(source, StringComparer.OrdinalIgnoreCase)
                && filter != WatchStatsSourceFilter.Combined)
                continue;

            var snapshot = await repository.BuildSourceSnapshotAsync(source, range, limit, ct);
            if (snapshot.Summary.TotalPlays > 0)
                sourceSnapshots.Add(snapshot);
        }

        WatchStatsSourceSnapshot? combined = null;
        if (WatchStatsSourceFilters.ShouldCollapse(filter)
            && prefs.Current.ShowCombinedWatchStats
            && enabledSources.Count > 0)
        {
            var keys = WatchStatsSourceFilters.ToSourceKeys(filter);
            var combinedSources = keys.Count == 0 || filter == WatchStatsSourceFilter.Combined
                ? enabledSources
                : enabledSources.Where(s => keys.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();
            if (combinedSources.Count > 0)
                combined = await repository.BuildCombinedSnapshotAsync(combinedSources, range, aliases, limit, ct);
        }

        return new WatchStatsSnapshot(
            range.Period,
            filter,
            combined?.Summary.TotalPlays > 0 ? combined : null,
            sourceSnapshots,
            DateTimeOffset.UtcNow,
            null);
    }

    private async Task<IReadOnlyList<string>> GetEnabledSourcesAsync(CancellationToken ct)
    {
        var configured = await availability.GetConfiguredSourcesAsync(ct);
        var sources = new List<string>();
        foreach (var source in configured)
        {
            if (!IsSourceVisible(source))
                continue;
            if (!prefs.IsServiceEnabled(source))
                continue;
            sources.Add(source);
        }

        return sources;
    }

    private static string MapFilterKey(WatchStatsSourceFilter filter) => filter switch
    {
        WatchStatsSourceFilter.Plex => WatchStatsSources.Plex,
        WatchStatsSourceFilter.Emby => WatchStatsSources.Emby,
        WatchStatsSourceFilter.Jellyfin => WatchStatsSources.Jellyfin,
        _ => "combined"
    };

    private bool IsSourceVisible(string source) => source switch
    {
        WatchStatsSources.Plex => prefs.Current.ShowPlexWatchStats,
        WatchStatsSources.Emby => prefs.Current.ShowEmbyWatchStats,
        WatchStatsSources.Jellyfin => prefs.Current.ShowJellyfinWatchStats,
        _ => true
    };
}
