using ArrDash.Models;

namespace ArrDash.Services;

public static class ActivityChartSeriesHelper
{
    public static readonly string[] ServerSeriesOrder = ["Plex", "Emby", "Jellyfin", "Trakt"];
    public static readonly string[] DeliverySeriesOrder = ["Direct play", "Direct stream", "Transcode"];

    public static readonly IReadOnlyDictionary<string, string> ServerSeriesHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Plex"] = "Play sessions on Plex (Tautulli)",
        ["Emby"] = "Play sessions on Emby (Tracearr)",
        ["Jellyfin"] = "Play sessions on Jellyfin (Tracearr)",
        ["Trakt"] = "Plays imported from Trakt history",
        ["Total"] = "Combined plays across all sources in the stack"
    };

    public static readonly IReadOnlyDictionary<string, string> DeliverySeriesHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Direct play"] = "Stream played from file with no remux or transcode",
        ["Direct stream"] = "Container remuxed but video/audio codecs unchanged",
        ["Transcode"] = "Server converted the stream — highest CPU/GPU load"
    };

    public static IReadOnlyList<ActivityChartPoint> BuildMultiSeriesChart(
        WatchStatsRange range,
        IEnumerable<(string Series, IReadOnlyList<(DateTime Date, double Count)> Daily)> sources)
    {
        var merged = new SortedDictionary<DateTime, Dictionary<string, double>>();
        foreach (var (series, daily) in sources)
        {
            foreach (var (date, count) in daily)
            {
                if (count <= 0)
                    continue;

                if (!merged.TryGetValue(date.Date, out var bucket))
                    bucket = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                bucket[series] = bucket.GetValueOrDefault(series) + count;
                merged[date.Date] = bucket;
            }
        }

        if (merged.Count == 0)
            return [];

        var buckets = AggregateBuckets(merged, range.Period);
        var points = new List<ActivityChartPoint>();
        foreach (var bucket in buckets)
        {
            foreach (var (series, value) in bucket.Values.Where(kv => kv.Value > 0).OrderBy(kv => SeriesRank(kv.Key)))
                points.Add(new ActivityChartPoint(bucket.Label, value, series));

            var total = bucket.Values.Values.Sum();
            if (total > 0)
                points.Add(new ActivityChartPoint(bucket.Label, total, "Total"));
        }

        return points;
    }

    public static IReadOnlyList<ActivityChartPoint> BuildConcurrentSeries(
        IReadOnlyList<ActivityConcurrentPoint> rows)
    {
        if (rows.Count == 0)
            return [];

        return rows.SelectMany(row => new[]
        {
            new ActivityChartPoint(row.Label, row.Direct, "Direct play"),
            new ActivityChartPoint(row.Label, row.DirectStream, "Direct stream"),
            new ActivityChartPoint(row.Label, row.Transcode, "Transcode")
        }).Where(p => p.Value > 0).ToList();
    }

    public static IReadOnlyList<ActivityChartPoint> SelectSeries(
        IReadOnlyList<ActivityChartPoint> points,
        string series) =>
        points.Where(p => string.Equals(p.Series, series, StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<ActivityChartPoint> ResolveSourceChartPoints(
        WatchStatsSourceFilter filter,
        IReadOnlyList<ActivityChartPoint>? tracearr,
        IReadOnlyList<ActivityChartPoint>? tautulli) =>
        filter switch
        {
            WatchStatsSourceFilter.Plex => tautulli ?? [],
            WatchStatsSourceFilter.Emby or WatchStatsSourceFilter.Jellyfin => tracearr ?? [],
            _ => MergeChartPoints(tracearr ?? [], tautulli ?? [])
        };

    public static IReadOnlyList<ActivityChartPoint> MergeChartPoints(
        IReadOnlyList<ActivityChartPoint> left,
        IReadOnlyList<ActivityChartPoint> right)
    {
        if (left.Count == 0)
            return right;
        if (right.Count == 0)
            return left;

        return left
            .Concat(right)
            .GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ActivityChartPoint(g.Key, g.Sum(p => p.Value), g.First().Series))
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    public static ActivityQualityStats? MergeQualityStats(ActivityQualityStats? left, ActivityQualityStats? right)
    {
        if (left is null)
            return right;
        if (right is null)
            return left;

        var directPlay = left.DirectPlay + right.DirectPlay;
        var directStream = left.DirectStream + right.DirectStream;
        var transcode = left.Transcode + right.Transcode;
        var total = directPlay + directStream + transcode;
        if (total <= 0)
            return null;

        return new ActivityQualityStats(
            directPlay,
            directStream,
            transcode,
            total,
            Percent(directPlay, total),
            Percent(directStream, total),
            Percent(transcode, total));
    }

    private static int Percent(int value, int total) =>
        total <= 0 ? 0 : (int)Math.Round(value * 100.0 / total);

    private static List<(string Label, Dictionary<string, double> Values)> AggregateBuckets(
        SortedDictionary<DateTime, Dictionary<string, double>> daily,
        WatchStatsPeriod period)
    {
        return period switch
        {
            WatchStatsPeriod.Year or WatchStatsPeriod.All => daily
                .GroupBy(kv => new DateTime(kv.Key.Year, kv.Key.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => (
                    g.Key.ToString("MMM yyyy"),
                    g.SelectMany(x => x.Value)
                        .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(grp => grp.Key, grp => grp.Sum(x => x.Value), StringComparer.OrdinalIgnoreCase)))
                .ToList(),
            WatchStatsPeriod.Today => daily
                .TakeLast(1)
                .Select(kv => (kv.Key.ToString("MMM d"), new Dictionary<string, double>(kv.Value, StringComparer.OrdinalIgnoreCase)))
                .ToList(),
            _ => daily
                .Select(kv => (kv.Key.ToString("MMM d"), new Dictionary<string, double>(kv.Value, StringComparer.OrdinalIgnoreCase)))
                .ToList()
        };
    }

    private static int SeriesRank(string series) => series switch
    {
        "Plex" => 0,
        "Emby" => 1,
        "Jellyfin" => 2,
        "Trakt" => 3,
        "Direct play" => 0,
        "Direct stream" => 1,
        "Transcode" => 2,
        "Total" => 99,
        _ => 50
    };

    /// <summary>
    /// Pie rows for Activity media mix. Supports warehouse shape (categories + single
    /// "Hours" series already in hours) and legacy Tautulli shape (series named
    /// Movies/TV/… with duration values in seconds).
    /// </summary>
    public static IReadOnlyList<ActivityChartPoint> BuildMediaMixPieRows(ActivityMediaMix mix)
    {
        if (mix.Series.Count == 1
            && string.Equals(mix.Series[0].Name, "Hours", StringComparison.OrdinalIgnoreCase)
            && mix.Categories.Count > 0)
        {
            var values = mix.Series[0].Values;
            return mix.Categories
                .Select((cat, i) => new ActivityChartPoint(
                    NormalizeMediaMixLabel(cat),
                    i < values.Count ? values[i] : 0))
                .Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value)
                .ToList();
        }

        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var series in mix.Series.Where(s =>
                     !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(s.Name, "Hours", StringComparison.OrdinalIgnoreCase)))
        {
            var name = NormalizeMediaMixLabel(series.Name);
            totals[name] = totals.GetValueOrDefault(name) + series.Values.Sum();
        }

        return totals
            .Select(kv => new ActivityChartPoint(kv.Key, kv.Value / 3600.0))
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    private static string NormalizeMediaMixLabel(string name) => name switch
    {
        "Movies" or "Movie" => "Movies",
        "TV" or "Episodes" or "Episode" => "TV",
        "Music" or "Tracks" => "Music",
        _ => name
    };
}
