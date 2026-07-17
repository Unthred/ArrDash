using ArrDash.Models;

namespace ArrDash.Services;

internal static class WatchStatsAggregation
{
    public static WatchStatsSourceSnapshot Combine(
        IReadOnlyList<WatchStatsSourceSnapshot> sources,
        IReadOnlyList<WatchStatsUserAlias> aliases,
        int limit)
    {
        if (sources.Count == 0)
        {
            return new WatchStatsSourceSnapshot(
                "combined",
                "Combined",
                true,
                null,
                new WatchStatsSummary(0, 0, 0, null),
                [],
                [],
                [],
                [],
                [],
                [],
                []);
        }

        var topUsers = MergeTopUsers(sources, aliases, limit);
        var lastPlays = sources
            .Select(s => s.Summary.LastPlayAt)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();
        var summary = new WatchStatsSummary(
            Math.Round(sources.Sum(s => s.Summary.TotalHours), 1),
            sources.Sum(s => s.Summary.TotalPlays),
            topUsers.Count,
            lastPlays.Count > 0 ? lastPlays.Max() : null);

        return new WatchStatsSourceSnapshot(
            "combined",
            "Combined",
            true,
            null,
            summary,
            topUsers,
            MergeByTitle(sources.SelectMany(s => s.TopGenres), limit, ActivityDrilldownKind.Genre),
            MergeByTitle(sources.SelectMany(s => s.TopMovies), limit, ActivityDrilldownKind.Media),
            MergeByTitle(sources.SelectMany(s => s.TopTv), limit, ActivityDrilldownKind.Media),
            MergeByTitle(sources.SelectMany(s => s.TopMusic), limit, ActivityDrilldownKind.Media),
            MergeByTitle(sources.SelectMany(s => s.TopPlatforms), limit, ActivityDrilldownKind.Platform),
            sources.SelectMany(s => s.Recent).Take(limit).ToList(),
            MergeByTitle(sources.SelectMany(s => s.TopPopularMovies ?? []), limit, ActivityDrilldownKind.Media),
            MergeByTitle(sources.SelectMany(s => s.TopPopularTv ?? []), limit, ActivityDrilldownKind.Media),
            MergeByTitle(sources.SelectMany(s => s.TopLibraries ?? []), limit, ActivityDrilldownKind.Library));
    }

    private static IReadOnlyList<WatchStatRow> MergeTopUsers(
        IReadOnlyList<WatchStatsSourceSnapshot> sources,
        IReadOnlyList<WatchStatsUserAlias> aliases,
        int limit)
    {
        var merged = new Dictionary<string, (double Hours, int Plays, List<string> Breakdown, string? ThumbUrl, string? DrilldownKey, ActivityDrilldownKind? DrilldownKind)>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var row in source.TopUsers)
            {
                if (string.IsNullOrWhiteSpace(row.Title)
                    || string.Equals(row.Title, "Unknown", StringComparison.OrdinalIgnoreCase))
                    continue;

                var canonical = ResolveCanonicalUser(source.Key, row.Title, aliases);
                if (!merged.TryGetValue(canonical, out var current))
                    current = (0, 0, [], null, null, null);

                current.Hours += row.Hours;
                current.Plays += row.Plays;
                current.Breakdown.Add($"{source.Label} {row.Hours:0.#}h");
                if (string.IsNullOrWhiteSpace(current.ThumbUrl) && !string.IsNullOrWhiteSpace(row.ThumbUrl))
                    current.ThumbUrl = row.ThumbUrl;
                current.DrilldownKey ??= row.DrilldownKey ?? canonical;
                current.DrilldownKind ??= row.DrilldownKind ?? ActivityDrilldownKind.User;
                merged[canonical] = current;
            }
        }

        return merged
            .Select(kv => new WatchStatRow(
                kv.Key,
                string.Join(" · ", kv.Value.Breakdown),
                Math.Round(kv.Value.Hours, 1),
                kv.Value.Plays,
                kv.Value.ThumbUrl,
                null,
                null,
                kv.Value.Breakdown,
                kv.Value.DrilldownKey,
                kv.Value.DrilldownKind))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> MergeByTitle(
        IEnumerable<WatchStatRow> rows,
        int limit,
        ActivityDrilldownKind? defaultKind = ActivityDrilldownKind.Media) =>
        rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Title)
                && !string.Equals(r.Title, "Unknown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var kind = g.Select(x => x.DrilldownKind).FirstOrDefault(k => k is not null) ?? defaultKind;
                var key = g.Select(x => x.DrilldownKey).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                    ?? (kind is not null ? g.Key : null);
                return new WatchStatRow(
                    g.Key,
                    g.Select(x => x.Subtitle).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                    Math.Round(g.Sum(x => x.Hours), 1),
                    g.Sum(x => x.Plays),
                    g.Select(x => x.ThumbUrl).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                    null,
                    g.Select(x => x.Source).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                    null,
                    key,
                    kind);
            })
            .OrderByDescending(r => r.Hours)
            .ThenByDescending(r => r.Plays)
            .Take(limit)
            .ToList();

    private static string ResolveCanonicalUser(string source, string user, IReadOnlyList<WatchStatsUserAlias> aliases)
    {
        var alias = aliases.FirstOrDefault(a =>
            string.Equals(a.Source, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.SourceUserName, user, StringComparison.OrdinalIgnoreCase));

        return alias?.CanonicalName ?? user;
    }
}
