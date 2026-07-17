using ArrDash.Models;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public sealed class UserActivityService(
    WatchStatsService watchStats,
    TracearrClient tracearr)
{
    private const int UserLimit = 50;

    private readonly object _cacheLock = new();
    private UserActivitySnapshot? _cached;
    private (WatchStatsRange Range, WatchStatsSourceFilter Filter) _cacheKey;
    private DateTimeOffset _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    public async Task<UserActivitySnapshot> GetAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        bool force = false,
        CancellationToken ct = default)
    {
        if (!range.IsQueryable)
            range = WatchStatsRange.Year;

        if (!force)
        {
            lock (_cacheLock)
            {
                if (_cached is not null
                    && WatchStatsPeriodHelper.RangesEqual(_cacheKey.Range, range)
                    && _cacheKey.Filter == filter
                    && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
                    return _cached;
            }
        }

        var watch = await watchStats.GetAsync(range, filter, force, ct, topLimit: UserLimit);
        var leaderboard = filter == WatchStatsSourceFilter.Combined
            ? watch.Combined
            : watch.Sources.FirstOrDefault(s =>
                string.Equals(s.Key, MapFilterKey(filter), StringComparison.OrdinalIgnoreCase));

        var topUsers = leaderboard?.TopUsers ?? [];
        var tracearrUsers = tracearr.IsConfigured
            ? await tracearr.GetUsersAsync(ct)
            : [];

        var metaLookup = tracearrUsers
            .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var totalHours = topUsers.Sum(u => u.Hours);
        var totalPlays = topUsers.Sum(u => u.Plays);

        var rows = topUsers
            .Select((user, index) => MapRow(user, index + 1, totalHours, metaLookup))
            .ToList();

        var snapshot = new UserActivitySnapshot(
            range.Period,
            filter,
            Math.Round(totalHours, 1),
            totalPlays,
            rows.Count,
            rows.Count > 0 ? Math.Round(totalHours / rows.Count, 1) : 0,
            rows.FirstOrDefault(),
            rows,
            rows
                .Where(r => r.Hours > 0)
                .Take(10)
                .Select(r => new ActivityChartPoint(r.Username, r.Hours))
                .ToList(),
            DateTimeOffset.UtcNow);

        lock (_cacheLock)
        {
            _cached = snapshot;
            _cacheKey = (range, filter);
            _cachedAt = DateTimeOffset.UtcNow;
        }

        return snapshot;
    }

    private static UserActivityRow MapRow(
        WatchStatRow user,
        int rank,
        double totalHours,
        IReadOnlyDictionary<string, List<TracearrUserInfo>> metaLookup)
    {
        metaLookup.TryGetValue(user.Title, out var matches);
        var meta = matches?.OrderByDescending(m => m.SessionCount).FirstOrDefault();

        return new UserActivityRow(
            rank,
            user.Title,
            meta?.DisplayName,
            user.ThumbUrl ?? meta?.AvatarUrl,
            user.Hours,
            user.Plays,
            totalHours > 0 ? Math.Round(user.Hours / totalHours * 100, 1) : 0,
            user.SourceBreakdown,
            user.DrilldownKey ?? user.Title,
            meta?.TrustScore,
            meta?.SessionCount,
            meta?.LastActivityAt,
            meta?.ServerName);
    }

    private static string MapFilterKey(WatchStatsSourceFilter filter) => filter switch
    {
        WatchStatsSourceFilter.Plex => WatchStatsSources.Plex,
        WatchStatsSourceFilter.Emby => WatchStatsSources.Emby,
        WatchStatsSourceFilter.Jellyfin => WatchStatsSources.Jellyfin,
        _ => "combined"
    };
}
