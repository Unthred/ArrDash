using System.Text.Json;
using ArrDash.Data.Entities;
using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Builds Activity charts and drilldowns from canonical <see cref="PlayEventEntity"/> rows only.
/// </summary>
public static class PlayEventAnalyticsService
{
    public static ActivityAnalyticsBundle Build(
        IReadOnlyList<PlayEventEntity> events,
        WatchStatsRange range,
        int limit = 10)
    {
        if (events.Count == 0)
            return ActivityAnalyticsBundle.Empty;

        var summary = new WatchStatsSummary(
            Math.Round(events.Sum(e => e.DurationSeconds) / 3600.0, 1),
            events.Count,
            events.Select(e => e.UserDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            events.Count > 0 ? new DateTimeOffset(events.Max(e => e.PlayedAtUtc), TimeSpan.Zero) : null);

        return new ActivityAnalyticsBundle(
            summary,
            BuildPlaysOverTime(events, range),
            BuildMediaMix(events),
            BuildByDayOfWeek(events),
            BuildByHourOfDay(events),
            BuildPlatforms(events),
            BuildQuality(events),
            BuildConcurrentStreams(events, range),
            BuildLeaderboard(events, limit));
    }

    public static ActivityDrilldownSnapshot BuildDrilldown(
        ActivityDrilldownRequest request,
        IReadOnlyList<PlayEventEntity> events,
        int breakdownLimit = ActivityDrilldownProfiler.BreakdownLimit,
        int playLimit = ActivityDrilldownProfiler.PlayLimit)
    {
        var profileEvents = events.Select(ActivityDrilldownProfiler.FromEntity).ToList();
        return ActivityDrilldownProfiler.Build(request, profileEvents, breakdownLimit, playLimit);
    }

    private static IReadOnlyList<ActivityChartPoint> BuildPlaysOverTime(
        IReadOnlyList<PlayEventEntity> events,
        WatchStatsRange range)
    {
        var (start, end) = range.Bounds();
        var endExclusive = end.Date.AddDays(1);

        if (WatchStatsPeriodHelper.UsesMonthlyBuckets(range.Period))
        {
            return events
                .Where(e => e.PlayedAtUtc >= start && e.PlayedAtUtc < endExclusive)
                .GroupBy(e => new DateTime(e.PlayedAtUtc.Year, e.PlayedAtUtc.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => new ActivityChartPoint(g.Key.ToString("MMM yyyy"), g.Count()))
                .ToList();
        }

        return events
            .Where(e => e.PlayedAtUtc >= start && e.PlayedAtUtc < endExclusive)
            .GroupBy(e => e.PlayedAtUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ActivityChartPoint(
                g.Key.ToString(range.Period == WatchStatsPeriod.Today ? "HH:mm" : "MMM d"),
                g.Count()))
            .ToList();
    }

    private static ActivityMediaMix? BuildMediaMix(IReadOnlyList<PlayEventEntity> events)
    {
        var movie = events.Where(e => e.MediaType == "movie").Sum(e => e.DurationSeconds) / 3600.0;
        var tv = events.Where(e => e.MediaType == "episode").Sum(e => e.DurationSeconds) / 3600.0;
        var music = events.Where(e => e.MediaType == "music").Sum(e => e.DurationSeconds) / 3600.0;
        var other = events.Where(e => e.MediaType is not ("movie" or "episode" or "music"))
            .Sum(e => e.DurationSeconds) / 3600.0;

        if (movie + tv + music + other <= 0)
            return null;

        return new ActivityMediaMix(
            [ "Movies", "TV", "Music", "Other" ],
            [
                new ActivityMediaSeries("Hours", [ Math.Round(movie, 1), Math.Round(tv, 1), Math.Round(music, 1), Math.Round(other, 1) ])
            ]);
    }

    private static IReadOnlyList<ActivityChartPoint> BuildByDayOfWeek(IReadOnlyList<PlayEventEntity> events)
    {
        var days = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var counts = new int[7];
        foreach (var ev in events)
            counts[(int)ev.PlayedAtUtc.DayOfWeek]++;

        return days.Select((label, i) => new ActivityChartPoint(label, counts[i])).ToList();
    }

    private static IReadOnlyList<ActivityChartPoint> BuildByHourOfDay(IReadOnlyList<PlayEventEntity> events)
    {
        var counts = new int[24];
        foreach (var ev in events)
            counts[ev.PlayedAtUtc.Hour]++;

        return Enumerable.Range(0, 24)
            .Select(h => new ActivityChartPoint($"{h:00}:00", counts[h]))
            .ToList();
    }

    private static IReadOnlyList<ActivityChartPoint> BuildPlatforms(IReadOnlyList<PlayEventEntity> events) =>
        events
            .GroupBy(e => ActivityPlatformHelper.Normalize(e.Platform ?? e.Client ?? "Unknown"), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ActivityChartPoint(g.Key, g.Count()))
            .OrderByDescending(p => p.Value)
            .ToList();

    private static (IReadOnlyList<ActivityChartPoint> Quality, ActivityQualityStats? Stats) BuildQuality(
        IReadOnlyList<PlayEventEntity> events)
    {
        var directPlay = 0;
        var directStream = 0;
        var transcode = 0;
        var unknown = 0;

        foreach (var ev in events)
        {
            switch (NormalizeDecision(ev.TranscodeDecision))
            {
                case "direct play": directPlay++; break;
                case "direct stream" or "copy": directStream++; break;
                case "transcode": transcode++; break;
                default: unknown++; break;
            }
        }

        var total = directPlay + directStream + transcode + unknown;
        if (total == 0)
            return ([], null);

        var points = new List<ActivityChartPoint>
        {
            new("Direct play", directPlay),
            new("Direct stream", directStream),
            new("Transcode", transcode)
        };
        if (unknown > 0)
            points.Add(new ActivityChartPoint("Unknown", unknown));

        var known = directPlay + directStream + transcode;
        var stats = known > 0
            ? new ActivityQualityStats(
                directPlay,
                directStream,
                transcode,
                known,
                (int)Math.Round(directPlay * 100.0 / known),
                (int)Math.Round(directStream * 100.0 / known),
                (int)Math.Round(transcode * 100.0 / known))
            : null;

        return (points, stats);
    }

    private static IReadOnlyList<ActivityConcurrentPoint> BuildConcurrentStreams(
        IReadOnlyList<PlayEventEntity> events,
        WatchStatsRange range)
    {
        var (rangeStart, rangeEnd) = range.Bounds();
        var endExclusive = rangeEnd.Date.AddDays(1);

        var intervals = events
            .Where(e => e.PlayedAtUtc >= rangeStart && e.PlayedAtUtc < endExclusive && e.DurationSeconds > 0)
            .Select(e => (
                Start: e.PlayedAtUtc,
                End: e.PlayedAtUtc.AddSeconds(Math.Max(e.DurationSeconds, 60)),
                Decision: NormalizeDecision(e.TranscodeDecision)))
            .ToList();

        if (intervals.Count == 0)
            return [];

        IEnumerable<(DateTime Bucket, double Total, double Direct, double DirectStream, double Transcode)> buckets;

        if (WatchStatsPeriodHelper.UsesMonthlyBuckets(range.Period))
        {
            buckets = intervals
                .GroupBy(i => new DateTime(i.Start.Year, i.Start.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => PeakForBucket(g.Select(x => (x.Start, x.End, x.Decision)), g.Key));
        }
        else
        {
            buckets = intervals
                .GroupBy(i => i.Start.Date)
                .OrderBy(g => g.Key)
                .Select(g => PeakForBucket(
                    g.Select(x => (x.Start, x.End, x.Decision)),
                    g.Key));
        }

        return buckets
            .Select(b => new ActivityConcurrentPoint(
                b.Bucket.ToString(WatchStatsPeriodHelper.UsesMonthlyBuckets(range.Period) ? "MMM yyyy" : "MMM d"),
                b.Total,
                b.Direct,
                b.DirectStream,
                b.Transcode))
            .ToList();
    }

    private static (DateTime Bucket, double Total, double Direct, double DirectStream, double Transcode) PeakForBucket(
        IEnumerable<(DateTime Start, DateTime End, string Decision)> intervals,
        DateTime bucket)
    {
        var events = new List<(DateTime At, int Delta, string Decision)>();
        foreach (var (start, end, decision) in intervals)
        {
            events.Add((start, 1, decision));
            events.Add((end, -1, decision));
        }

        events.Sort((a, b) =>
        {
            var cmp = a.At.CompareTo(b.At);
            return cmp != 0 ? cmp : a.Delta.CompareTo(b.Delta);
        });

        var total = 0;
        var direct = 0;
        var directStream = 0;
        var transcode = 0;
        var peakTotal = 0.0;
        var peakDirect = 0.0;
        var peakDirectStream = 0.0;
        var peakTranscode = 0.0;

        foreach (var (at, delta, decision) in events)
        {
            total += delta;
            switch (decision)
            {
                case "direct play": direct += delta; break;
                case "direct stream" or "copy": directStream += delta; break;
                case "transcode": transcode += delta; break;
            }

            if (total > peakTotal)
            {
                peakTotal = total;
                peakDirect = direct;
                peakDirectStream = directStream;
                peakTranscode = transcode;
            }
        }

        return (bucket, peakTotal, peakDirect, peakDirectStream, peakTranscode);
    }

    private static WatchStatsSourceSnapshot BuildLeaderboard(IReadOnlyList<PlayEventEntity> events, int limit)
    {
        var summary = new WatchStatsSummary(
            Math.Round(events.Sum(e => e.DurationSeconds) / 3600.0, 1),
            events.Count,
            events.Select(e => e.UserDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            events.Count > 0 ? new DateTimeOffset(events.Max(e => e.PlayedAtUtc), TimeSpan.Zero) : null);

        return new WatchStatsSourceSnapshot(
            "warehouse",
            "Play history",
            true,
            null,
            summary,
            BuildTopUsers(events, limit),
            BuildTopGenres(events, limit),
            BuildTopMedia(events, "movie", limit, popular: false),
            BuildTopMedia(events, "episode", limit, popular: false),
            BuildTopMedia(events, "music", limit, popular: false),
            BuildTopPlatforms(events, limit),
            BuildRecent(events, limit),
            BuildTopMedia(events, "movie", limit, popular: true),
            BuildTopMedia(events, "episode", limit, popular: true),
            BuildTopLibraries(events, limit));
    }

    private static IReadOnlyList<WatchStatRow> BuildTopUsers(IReadOnlyList<PlayEventEntity> events, int limit) =>
        events.GroupBy(e => e.UserDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                null,
                null,
                g.First().Source,
                DrilldownKey: g.Select(e => e.UserExternalId).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? g.Key,
                DrilldownKind: ActivityDrilldownKind.User))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopGenres(IReadOnlyList<PlayEventEntity> events, int limit)
    {
        var genreHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genrePlays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in events)
        {
            foreach (var genre in ReadGenres(ev))
            {
                genreHours[genre] = genreHours.GetValueOrDefault(genre) + ev.DurationSeconds / 3600.0;
                genrePlays[genre] = genrePlays.GetValueOrDefault(genre) + 1;
            }
        }

        return genreHours
            .Select(kv => new WatchStatRow(
                kv.Key,
                $"{genrePlays[kv.Key]} plays",
                Math.Round(kv.Value, 1),
                genrePlays[kv.Key],
                null,
                null,
                null,
                DrilldownKey: kv.Key,
                DrilldownKind: ActivityDrilldownKind.Genre))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> BuildTopMedia(
        IReadOnlyList<PlayEventEntity> events,
        string mediaType,
        int limit,
        bool popular)
    {
        var filtered = events.Where(e => string.Equals(e.MediaType, mediaType, StringComparison.OrdinalIgnoreCase));

        if (popular)
        {
            return filtered
                .GroupBy(MediaGroupKey, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var viewers = g.Select(e => e.UserDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    return new WatchStatRow(
                        g.First().Title,
                        $"{viewers} viewers",
                        Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                        viewers,
                        BuildThumb(g.First()),
                        null,
                        g.First().Source,
                        DrilldownKey: MediaDrilldownKey(g.First()),
                        DrilldownKind: ActivityDrilldownKind.Media);
                })
                .OrderByDescending(r => r.Plays)
                .ThenByDescending(r => r.Hours)
                .Take(limit)
                .ToList();
        }

        return filtered
            .GroupBy(MediaGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.First().Title,
                mediaType == "episode" ? g.Select(e => e.SeriesTitle).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) : null,
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                BuildThumb(g.First()),
                null,
                g.First().Source,
                DrilldownKey: MediaDrilldownKey(g.First()),
                DrilldownKind: ActivityDrilldownKind.Media))
            .OrderByDescending(r => r.Hours)
            .ThenByDescending(r => r.Plays)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> BuildTopLibraries(IReadOnlyList<PlayEventEntity> events, int limit) =>
        events
            .Where(e => !string.IsNullOrWhiteSpace(e.LibraryName) || !string.IsNullOrWhiteSpace(e.LibraryExternalId))
            .GroupBy(e => e.LibraryName ?? $"Library {e.LibraryExternalId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                null,
                null,
                g.First().Source,
                DrilldownKey: g.Select(e => e.LibraryExternalId).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? g.Key,
                DrilldownKind: ActivityDrilldownKind.Library))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopPlatforms(IReadOnlyList<PlayEventEntity> events, int limit) =>
        events.GroupBy(e => e.Platform ?? e.Client ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                null,
                null,
                g.First().Source,
                DrilldownKey: g.Key,
                DrilldownKind: ActivityDrilldownKind.Platform))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildRecent(IReadOnlyList<PlayEventEntity> events, int limit) =>
        events
            .OrderByDescending(e => e.PlayedAtUtc)
            .GroupBy(RecentGroupKey)
            .Select(g => g.First())
            .Take(limit)
            .Select(e => new WatchStatRow(
                e.Title,
                $"{e.UserDisplayName} · {WatchStatsSources.Label(e.Source)}",
                Math.Round(e.DurationSeconds / 3600.0, 1),
                1,
                BuildThumb(e),
                null,
                e.Source,
                DrilldownKey: MediaDrilldownKey(e),
                DrilldownKind: ActivityDrilldownKind.Media))
            .ToList();

    private static string MediaGroupKey(PlayEventEntity e) =>
        string.Equals(e.MediaType, "episode", StringComparison.OrdinalIgnoreCase)
            ? (e.SeriesTitle ?? e.Title)
            : e.Title;

    public static string MediaDrilldownKey(PlayEventEntity e)
    {
        if (!string.IsNullOrWhiteSpace(e.GrandparentExternalId))
            return $"g:{e.GrandparentExternalId}";

        if (string.Equals(e.MediaType, "movie", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(e.ExternalItemId))
            return $"r:{e.ExternalItemId}";

        return e.SeriesTitle ?? e.Title;
    }

    private static string RecentGroupKey(PlayEventEntity e) =>
        !string.IsNullOrWhiteSpace(e.ExternalPlayId)
            ? $"id:{e.Source}:{e.ExternalPlayId}"
            : $"t:{e.Source}:{MediaGroupKey(e)}:{e.UserDisplayName}";

    private static string? BuildThumb(PlayEventEntity e)
    {
        if (string.IsNullOrWhiteSpace(e.ThumbPath) && string.IsNullOrWhiteSpace(e.ExternalItemId))
            return null;

        return e.Source switch
        {
            WatchStatsSources.Plex when !string.IsNullOrWhiteSpace(e.ThumbPath) => PosterUrls.PlexThumb(e.ThumbPath),
            WatchStatsSources.Emby when !string.IsNullOrWhiteSpace(e.ExternalItemId) => PosterUrls.EmbyItem(e.ExternalItemId),
            WatchStatsSources.Jellyfin when !string.IsNullOrWhiteSpace(e.ExternalItemId) => PosterUrls.JellyfinItem(e.ExternalItemId),
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadGenres(PlayEventEntity entity)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(entity.GenresJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeDecision(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? "";
        return value switch
        {
            "direct play" or "directplay" or "direct_play" => "direct play",
            "direct stream" or "directstream" or "direct_stream" or "copy" => "direct stream",
            "transcode" or "transcoding" => "transcode",
            _ => value
        };
    }
}

public sealed record ActivityAnalyticsBundle(
    WatchStatsSummary Summary,
    IReadOnlyList<ActivityChartPoint> PlaysOverTime,
    ActivityMediaMix? MediaMix,
    IReadOnlyList<ActivityChartPoint> ByDayOfWeek,
    IReadOnlyList<ActivityChartPoint> ByHourOfDay,
    IReadOnlyList<ActivityChartPoint> Platforms,
    (IReadOnlyList<ActivityChartPoint> Quality, ActivityQualityStats? Stats) QualityBundle,
    IReadOnlyList<ActivityConcurrentPoint> ConcurrentStreams,
    WatchStatsSourceSnapshot Leaderboard)
{
    public static ActivityAnalyticsBundle Empty { get; } = new(
        new WatchStatsSummary(0, 0, 0, null),
        [],
        null,
        [],
        [],
        [],
        ([], null),
        [],
        new WatchStatsSourceSnapshot(
            "warehouse", "Play history", true, null,
            new WatchStatsSummary(0, 0, 0, null),
            [], [], [], [], [], [], [],
            [], [], []));
}
