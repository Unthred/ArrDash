using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services.Clients;

public sealed class TracearrClient(HttpClient http, MediaServiceOptionsAccessor options)
{
    private ServiceEndpoint Tracearr => options.Options.Tracearr;
    private Dictionary<string, string>? _serverTypeById;
    private DateTimeOffset _serverTypeCachedAt;

    public bool IsConfigured => Tracearr.IsConfigured;

    public async Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, "Tracearr URL or API key required");

        try
        {
            using var response = await SendAsync(HttpMethod.Get, "/api/v1/public/health", ct);
            return response.IsSuccessStatusCode ? (true, "Connected") : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<TracearrServerInfo>> GetServersAsync(CancellationToken ct)
    {
        var doc = await GetJsonAsync("/api/v1/public/health", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array)
            return [];

        return servers.EnumerateArray()
            .Select(s => new TracearrServerInfo(
                ReadString(s, "id") ?? "",
                ReadString(s, "name") ?? "Unknown",
                ReadString(s, "type") ?? "unknown"))
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .ToList();
    }

    public async Task<TracearrDashboardStats?> GetDashboardStatsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var doc = await GetJsonAsync("/api/v1/public/stats", ct);
        if (doc is null)
            return null;

        var root = doc.RootElement;
        return new TracearrDashboardStats(
            ReadInt(root, "activeStreams") ?? 0,
            ReadInt(root, "totalUsers") ?? 0,
            ReadInt(root, "totalSessions") ?? 0,
            ReadInt(root, "recentViolations") ?? 0);
    }

    public async Task<ActivityTodayStats?> GetStatsTodayAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var doc = await GetJsonAsync("/api/v1/public/stats/today", ct);
        if (doc is null)
            return null;

        var root = doc.RootElement;
        return new ActivityTodayStats(
            ReadInt(root, "todayPlays") ?? 0,
            ReadDouble(root, "watchTimeHours") ?? 0,
            ReadInt(root, "activeUsersToday") ?? 0,
            ReadInt(root, "alertsLast24h") ?? 0);
    }

    public async Task<IReadOnlyList<TracearrUserInfo>> GetUsersAsync(CancellationToken ct, int maxUsers = 100)
    {
        if (!IsConfigured)
            return [];

        var results = new List<TracearrUserInfo>();
        var page = 1;
        const int pageSize = 100;

        while (results.Count < maxUsers)
        {
            var doc = await GetJsonAsync($"/api/v1/public/users?page={page}&pageSize={pageSize}", ct);
            if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                break;

            var pageCount = 0;
            foreach (var row in data.EnumerateArray())
            {
                var username = ReadString(row, "username");
                if (string.IsNullOrWhiteSpace(username))
                    continue;

                results.Add(new TracearrUserInfo(
                    ReadString(row, "id") ?? "",
                    username,
                    ReadString(row, "displayName", "display_name"),
                    ResolveImageUrl(ReadString(row, "avatarUrl", "thumbUrl")),
                    ReadString(row, "serverName", "server_name"),
                    ReadString(row, "serverId", "server_id"),
                    ReadInt(row, "sessionCount", "session_count") ?? 0,
                    ReadInt(row, "trustScore", "trust_score") ?? 0,
                    ReadInt(row, "totalViolations", "total_violations") ?? 0,
                    ReadDate(row, "lastActivityAt", "last_activity_at")));

                pageCount++;
            }

            if (pageCount < pageSize)
                break;

            page++;
        }

        return results.Take(maxUsers).ToList();
    }

    public async Task<IReadOnlyList<(DateTime Date, double Count)>> GetDailyPlaysAsync(
        WatchStatsRange range,
        string? sourceKey,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return [];

        var tracearrPeriod = MapTracearrPeriod(range.Period) ?? "week";
        var (rangeStart, rangeEnd) = range.Bounds();
        var merged = new Dictionary<DateTime, double>();

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            var doc = await GetJsonAsync($"/api/v1/public/activity?period={tracearrPeriod}&timezone=UTC", ct);
            if (doc is not null)
                MergeDailyPlays(merged, doc.RootElement, rangeStart, rangeEnd);
            return merged.Select(kv => (kv.Key, kv.Value)).OrderBy(kv => kv.Key).ToList();
        }

        var servers = await GetServersAsync(ct);
        var serverIds = servers
            .Where(s => string.Equals(MapServerType(s.Type), sourceKey, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        foreach (var serverId in serverIds)
        {
            var doc = await GetJsonAsync($"/api/v1/public/activity?period={tracearrPeriod}&serverId={Uri.EscapeDataString(serverId)}&timezone=UTC", ct);
            if (doc is not null)
                MergeDailyPlays(merged, doc.RootElement, rangeStart, rangeEnd);
        }

        return merged.Select(kv => (kv.Key, kv.Value)).OrderBy(kv => kv.Key).ToList();
    }

    public async Task<IReadOnlyList<ActivityChartPoint>> GetPlatformsAsync(
        WatchStatsRange range,
        string? sourceKey,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return [];

        var tracearrPeriod = MapTracearrPeriod(range.Period) ?? "week";
        var merged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            var doc = await GetJsonAsync($"/api/v1/public/activity?period={tracearrPeriod}&timezone=UTC", ct);
            if (doc is not null)
                MergePlatforms(merged, doc.RootElement);
        }
        else
        {
            var servers = await GetServersAsync(ct);
            var serverIds = servers
                .Where(s => string.Equals(MapServerType(s.Type), sourceKey, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            foreach (var serverId in serverIds)
            {
                var doc = await GetJsonAsync(
                    $"/api/v1/public/activity?period={tracearrPeriod}&serverId={Uri.EscapeDataString(serverId)}&timezone=UTC",
                    ct);
                if (doc is not null)
                    MergePlatforms(merged, doc.RootElement);
            }
        }

        return merged
            .Select(kv => new ActivityChartPoint(ActivityPlatformHelper.Normalize(kv.Key), kv.Value))
            .GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ActivityChartPoint(g.Key, g.Sum(p => p.Value)))
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    private static void MergePlatforms(Dictionary<string, double> merged, JsonElement root)
    {
        if (!root.TryGetProperty("platforms", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var row in arr.EnumerateArray())
        {
            var name = ReadString(row, "platform", "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var count = ReadInt(row, "count") ?? ReadInt(row, "plays") ?? 0;
            if (count <= 0)
                continue;

            merged[name] = merged.GetValueOrDefault(name) + count;
        }
    }

    private static void MergeDailyPlays(
        Dictionary<DateTime, double> merged,
        JsonElement root,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        if (!root.TryGetProperty("plays", out var playsEl) || playsEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var row in playsEl.EnumerateArray())
        {
            var raw = ReadString(row, "date");
            if (!DateTime.TryParse(raw, out var dt))
                continue;

            var date = dt.Date;
            if (date < rangeStart || date > rangeEnd)
                continue;

            var count = ReadInt(row, "count") ?? 0;
            if (count <= 0)
                continue;

            merged[date] = merged.GetValueOrDefault(date) + count;
        }
    }

    public async Task<TracearrActivityData?> GetActivityAsync(WatchStatsRange range, string? sourceKey = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        var tracearrPeriod = MapTracearrPeriod(range.Period) ?? "week";

        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            var doc = await GetJsonAsync($"/api/v1/public/activity?period={tracearrPeriod}&timezone=UTC", ct);
            return doc is null ? null : ParseActivity(doc.RootElement, range);
        }

        var servers = await GetServersAsync(ct);
        var serverIds = servers
            .Where(s => string.Equals(MapServerType(s.Type), sourceKey, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

        if (serverIds.Count == 0)
            return null;

        TracearrActivityData? merged = null;
        foreach (var serverId in serverIds)
        {
            var doc = await GetJsonAsync($"/api/v1/public/activity?period={tracearrPeriod}&serverId={Uri.EscapeDataString(serverId)}&timezone=UTC", ct);
            if (doc is null)
                continue;

            var parsed = ParseActivity(doc.RootElement, range);
            if (parsed is null)
                continue;

            merged = merged is null ? parsed : MergeActivityData(merged, parsed);
        }

        return merged;
    }

    private static TracearrActivityData MergeActivityData(TracearrActivityData left, TracearrActivityData right) =>
        new(
            MergeChartPoints(left.PlaysOverTime, right.PlaysOverTime),
            MergeChartPoints(left.ByDayOfWeek, right.ByDayOfWeek),
            MergeChartPoints(left.ByHourOfDay, right.ByHourOfDay),
            MergeChartPoints(left.Platforms, right.Platforms),
            MergeChartPoints(left.Quality, right.Quality),
            MergeQualityStats(left.QualityStats, right.QualityStats),
            MergeConcurrentStreams(left.ConcurrentStreams, right.ConcurrentStreams));

    private static IReadOnlyList<ActivityChartPoint> MergeChartPoints(
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

    private static ActivityQualityStats? MergeQualityStats(ActivityQualityStats? left, ActivityQualityStats? right)
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

    private static IReadOnlyList<ActivityConcurrentPoint> MergeConcurrentStreams(
        IReadOnlyList<ActivityConcurrentPoint> left,
        IReadOnlyList<ActivityConcurrentPoint> right)
    {
        if (left.Count == 0)
            return right;
        if (right.Count == 0)
            return left;

        return left
            .Concat(right)
            .GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ActivityConcurrentPoint(
                g.Key,
                g.Sum(p => p.Total),
                g.Sum(p => p.Direct),
                g.Sum(p => p.DirectStream),
                g.Sum(p => p.Transcode)))
            .OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TracearrActivityData?> GetActivityAsync(WatchStatsRange range, CancellationToken ct) =>
        await GetActivityAsync(range, null, ct);

    private static TracearrActivityData? ParseActivity(JsonElement root, WatchStatsRange range)
    {
        var (rangeStart, rangeEnd) = range.Bounds();
        var daily = new List<(DateTime Date, double Count)>();
        if (root.TryGetProperty("plays", out var playsEl) && playsEl.ValueKind == JsonValueKind.Array)
        {
            daily = playsEl.EnumerateArray()
                .Select(p =>
                {
                    var raw = ReadString(p, "date");
                    if (!DateTime.TryParse(raw, out var dt))
                        return ((DateTime?)null, 0.0);
                    return (dt.Date, (double)(ReadInt(p, "count") ?? 0));
                })
                .Where(x => x.Item1 is not null && x.Item2 > 0)
                .Where(x => x.Item1!.Value >= rangeStart && x.Item1.Value <= rangeEnd)
                .GroupBy(x => x.Item1!.Value)
                .Select(g => (g.Key, g.Sum(x => x.Item2)))
                .OrderBy(x => x.Key)
                .ToList();
        }

        return new TracearrActivityData(
            AggregatePlaysOverTime(daily, range.Period),
            ParseDayOfWeek(root),
            ParseHourOfDay(root),
            ParseChartRows(root, "platforms", "platform", "count", "plays"),
            ParseQualityChart(root),
            ParseQualityStats(root),
            ParseConcurrentStreams(root, range));
    }

    private static IReadOnlyList<ActivityChartPoint> AggregatePlaysOverTime(
        IReadOnlyList<(DateTime Date, double Count)> daily,
        WatchStatsPeriod period)
    {
        if (daily.Count == 0)
            return [];

        return period switch
        {
            WatchStatsPeriod.Today => daily
                .TakeLast(1)
                .Select(d => new ActivityChartPoint(d.Date.ToString("MMM d"), d.Count))
                .ToList(),
            WatchStatsPeriod.Year or WatchStatsPeriod.All => daily
                .GroupBy(d => new DateTime(d.Date.Year, d.Date.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => new ActivityChartPoint(g.Key.ToString("MMM yyyy"), g.Sum(x => x.Count)))
                .ToList(),
            // Week / Month: one point per day
            _ => daily
                .Select(d => new ActivityChartPoint(d.Date.ToString("MMM d"), d.Count))
                .ToList()
        };
    }

    private static IReadOnlyList<ActivityChartPoint> ParseChartRows(
        JsonElement root,
        string arrayName,
        string labelKey,
        params string[] valueKeys)
    {
        if (!root.TryGetProperty(arrayName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray()
            .Select(row =>
            {
                var label = ReadString(row, labelKey, "name", "day", "hour", "platform") ?? "?";
                var value = 0.0;
                foreach (var key in valueKeys)
                {
                    var v = ReadInt(row, key);
                    if (v is > 0)
                    {
                        value = v.Value;
                        break;
                    }
                }

                if (value <= 0)
                    value = ReadInt(row, "durationMs", "duration_ms") ?? 0;

                return new ActivityChartPoint(label, value);
            })
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ToList();
    }

    private static IReadOnlyList<ActivityChartPoint> ParseDayOfWeek(JsonElement root)
    {
        var counts = new Dictionary<int, int>
        {
            [0] = 0, [1] = 0, [2] = 0, [3] = 0, [4] = 0, [5] = 0, [6] = 0
        };

        if (root.TryGetProperty("byDayOfWeek", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in arr.EnumerateArray())
            {
                var day = ReadInt(row, "day");
                if (day is < 0 or > 6)
                    continue;

                counts[day.Value] = ReadInt(row, "count") ?? 0;
            }
        }

        return counts
            .OrderBy(kv => kv.Key)
            .Select(kv => new ActivityChartPoint(DayLabel(kv.Key), kv.Value))
            .ToList();
    }

    private static string DayLabel(int day) => day switch
    {
        0 => "Sun",
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        _ => "?"
    };

    private static IReadOnlyList<ActivityChartPoint> ParseHourOfDay(JsonElement root)
    {
        var counts = Enumerable.Range(0, 24).ToDictionary(h => h, _ => 0);

        if (root.TryGetProperty("byHourOfDay", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in arr.EnumerateArray())
            {
                var hour = ReadInt(row, "hour");
                if (hour is < 0 or > 23)
                    continue;

                counts[hour.Value] = ReadInt(row, "count") ?? 0;
            }
        }

        return counts
            .OrderBy(kv => kv.Key)
            .Select(kv => new ActivityChartPoint(FormatHour(kv.Key), kv.Value))
            .ToList();
    }

    private static string FormatHour(int hour) => hour switch
    {
        0 => "12a",
        < 12 => $"{hour}a",
        12 => "12p",
        _ => $"{hour - 12}p"
    };

    private static IReadOnlyList<ActivityChartPoint> ParseQualityChart(JsonElement root)
    {
        var stats = ParseQualityStats(root);
        if (stats is null || stats.Total <= 0)
            return [];

        return
        [
            new ActivityChartPoint("Direct play", stats.DirectPlay),
            new ActivityChartPoint("Direct stream", stats.DirectStream),
            new ActivityChartPoint("Transcode", stats.Transcode)
        ];
    }

    private static ActivityQualityStats? ParseQualityStats(JsonElement root)
    {
        if (!root.TryGetProperty("quality", out var quality) || quality.ValueKind != JsonValueKind.Object)
            return null;

        return new ActivityQualityStats(
            ReadInt(quality, "directPlay") ?? 0,
            ReadInt(quality, "directStream") ?? 0,
            ReadInt(quality, "transcode") ?? 0,
            ReadInt(quality, "total") ?? 0,
            ReadInt(quality, "directPlayPercent") ?? 0,
            ReadInt(quality, "directStreamPercent") ?? 0,
            ReadInt(quality, "transcodePercent") ?? 0);
    }

    private static IReadOnlyList<ActivityConcurrentPoint> ParseConcurrentStreams(
        JsonElement root,
        WatchStatsRange range)
    {
        if (!root.TryGetProperty("concurrent", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var (rangeStart, rangeEnd) = range.Bounds();
        var buckets = arr.EnumerateArray()
            .Select(row =>
            {
                var raw = ReadString(row, "date");
                if (!DateTime.TryParse(raw, out var dt))
                    return ((DateTime?)null, Total: 0.0, Direct: 0.0, DirectStream: 0.0, Transcode: 0.0);
                return (
                    dt.Date,
                    (double)(ReadInt(row, "total") ?? 0),
                    (double)(ReadInt(row, "direct") ?? 0),
                    (double)(ReadInt(row, "directStream") ?? 0),
                    (double)(ReadInt(row, "transcode") ?? 0));
            })
            .Where(x => x.Item1 is not null)
            .Where(x => x.Item1!.Value >= rangeStart && x.Item1.Value <= rangeEnd)
            .GroupBy(x => x.Item1!.Value)
            .Select(g => (
                Date: g.Key,
                Total: g.Max(x => x.Total),
                Direct: g.Max(x => x.Direct),
                DirectStream: g.Max(x => x.DirectStream),
                Transcode: g.Max(x => x.Transcode)))
            .OrderBy(x => x.Date)
            .ToList();

        IEnumerable<(string Label, double Total, double Direct, double DirectStream, double Transcode)> aggregated = range.Period switch
        {
            WatchStatsPeriod.Year or WatchStatsPeriod.All => buckets
                .GroupBy(b => new DateTime(b.Date.Year, b.Date.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => (
                    g.Key.ToString("MMM yyyy"),
                    g.Max(x => x.Total),
                    g.Max(x => x.Direct),
                    g.Max(x => x.DirectStream),
                    g.Max(x => x.Transcode))),
            _ => buckets.Select(b => (
                b.Date.ToString(range.Period == WatchStatsPeriod.Today ? "HH:mm" : "MMM d"),
                b.Total,
                b.Direct,
                b.DirectStream,
                b.Transcode))
        };

        return aggregated
            .Select(x => new ActivityConcurrentPoint(x.Label, x.Total, x.Direct, x.DirectStream, x.Transcode))
            .ToList();
    }

    public async Task<IReadOnlyList<ImportedPlayEvent>> FetchImportedHistoryAsync(
        string sourceKey,
        DateTimeOffset sinceUtc,
        int maxRows,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        var servers = await GetServersAsync(ct);
        var serverIds = servers
            .Where(s => MapServerType(s.Type) == sourceKey)
            .Select(s => s.Id)
            .ToList();

        if (serverIds.Count == 0)
            return [];

        var endDate = DateTime.UtcNow.Date;
        var startDate = sinceUtc.UtcDateTime.Date;
        var history = new List<TracearrHistoryRow>();
        foreach (var serverId in serverIds)
            history.AddRange(await FetchHistoryAsync(serverId, startDate, endDate, maxRows, ct));

        return history
            .Where(h => h.PlayedAtUtc >= sinceUtc && IsImportableHistoryRow(h))
            .Select(h => MapImportedEvent(h, sourceKey))
            .DistinctBy(h => h.ExternalPlayId)
            .ToList();
    }

    private static bool IsImportableHistoryRow(TracearrHistoryRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Title) || string.Equals(row.Title, "Unknown", StringComparison.OrdinalIgnoreCase))
            return false;
        // Incomplete live segments often have 0 duration; keep stopped sessions with real watch time.
        if (row.DurationSeconds <= 0 && !string.Equals(row.State, "stopped", StringComparison.OrdinalIgnoreCase))
            return false;
        if (row.DurationSeconds <= 0)
            return false;
        return true;
    }

    private static ImportedPlayEvent MapImportedEvent(TracearrHistoryRow row, string sourceKey)
    {
        var playId = !string.IsNullOrWhiteSpace(row.SessionId)
            ? row.SessionId
            : $"{row.UserId ?? row.UserDisplayName}:{row.PlayedAtUtc.ToUnixTimeSeconds()}:{row.Title}";

        return new ImportedPlayEvent(
            sourceKey,
            $"tracearr:{playId}",
            row.UserDisplayName,
            row.UserId,
            row.Title,
            row.SeriesTitle,
            row.MediaType,
            [],
            row.Client,
            row.Platform,
            row.PlayedAtUtc,
            row.DurationSeconds,
            row.ItemId,
            row.PosterUrl,
            row.EpisodeTitle,
            TranscodeDecision: row.TranscodeDecision,
            SeasonNumber: row.SeasonNumber,
            EpisodeNumber: row.EpisodeNumber);
    }

    public async Task<WatchStatsSourceSnapshot?> BuildSnapshotAsync(
        string sourceKey,
        WatchStatsRange range,
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var servers = await GetServersAsync(ct);
            var serverIds = servers
                .Where(s => MapServerType(s.Type) == sourceKey)
                .Select(s => s.Id)
                .ToList();

            if (serverIds.Count == 0)
            {
                return new WatchStatsSourceSnapshot(
                    sourceKey,
                    WatchStatsSources.Label(sourceKey),
                    true,
                    "No Tracearr server configured for this source",
                    new WatchStatsSummary(0, 0, 0, null),
                    [],
                    [],
                    [],
                    [],
                    [],
                    [],
                    []);
            }

            var (startDate, endDate) = range.Bounds();
            var history = new List<TracearrHistoryRow>();
            foreach (var serverId in serverIds)
                history.AddRange(await FetchHistoryAsync(serverId, startDate, endDate, Math.Min(limit * 20, 500), ct));

            if (history.Count == 0)
            {
                var platforms = await FetchPlatformsAsync(range.Period, serverIds, ct);
                return new WatchStatsSourceSnapshot(
                    sourceKey,
                    WatchStatsSources.Label(sourceKey),
                    true,
                    null,
                    new WatchStatsSummary(0, 0, 0, null),
                    [],
                    [],
                    [],
                    [],
                    [],
                    platforms,
                    []);
            }

            var events = history.Select(h => MapEvent(h, MapServerType(h.ServerType))).ToList();
            return BuildSnapshotFromEvents(sourceKey, events, limit);
        }
        catch (Exception ex)
        {
            return new WatchStatsSourceSnapshot(
                sourceKey,
                WatchStatsSources.Label(sourceKey),
                false,
                ex.Message,
                new WatchStatsSummary(0, 0, 0, null),
                [],
                [],
                [],
                [],
                [],
                [],
                []);
        }
    }

    public async Task<WatchStatsSourceSnapshot?> BuildCombinedTracearrSnapshotAsync(
        WatchStatsRange range,
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var (startDate, endDate) = range.Bounds();
            var history = await FetchHistoryAsync(null, startDate, endDate, Math.Min(limit * 30, 800), ct);
            if (history.Count == 0)
                return null;

            var events = history.Select(h => MapEvent(h, MapServerType(h.ServerType))).ToList();
            return BuildSnapshotFromEvents("tracearr", events, limit) with
            {
                Key = "combined-tracearr",
                Label = "Tracearr"
            };
        }
        catch
        {
            return null;
        }
    }

    private WatchStatsSourceSnapshot BuildSnapshotFromEvents(
        string sourceKey,
        IReadOnlyList<TracearrPlayEvent> events,
        int limit)
    {
        var summary = new WatchStatsSummary(
            Math.Round(events.Sum(e => e.DurationSeconds) / 3600.0, 1),
            events.Count,
            events.Select(e => e.UserDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            events.Count > 0 ? events.Max(e => e.PlayedAtUtc) : null);

        return new WatchStatsSourceSnapshot(
            sourceKey,
            WatchStatsSources.Label(sourceKey),
            true,
            null,
            summary,
            BuildTopUsers(events, limit),
            [],
            BuildTopMedia(events, "movie", limit),
            BuildTopMedia(events, "episode", limit),
            BuildTopMedia(events, "music", limit),
            BuildTopPlatforms(events, limit),
            BuildRecent(events, limit));
    }

    private async Task<IReadOnlyList<WatchStatRow>> FetchPlatformsAsync(
        WatchStatsPeriod period,
        IReadOnlyList<string> serverIds,
        CancellationToken ct)
    {
        var tracearrPeriod = MapTracearrPeriod(period);
        if (tracearrPeriod is null)
            return [];

        var merged = new Dictionary<string, (int Plays, double Hours)>(StringComparer.OrdinalIgnoreCase);
        foreach (var serverId in serverIds)
        {
            var doc = await GetJsonAsync($"/api/v1/public/activity?period={tracearrPeriod}&serverId={serverId}", ct);
            if (doc is null || !doc.RootElement.TryGetProperty("platforms", out var platforms) || platforms.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var row in platforms.EnumerateArray())
            {
                var name = ReadString(row, "platform", "name") ?? "Unknown";
                var plays = ReadInt(row, "plays", "count") ?? 0;
                var hours = (ReadInt(row, "durationMs", "duration_ms") ?? 0) / 3600000.0;
                if (!merged.TryGetValue(name, out var current))
                    current = (0, 0);
                merged[name] = (current.Plays + plays, current.Hours + hours);
            }
        }

        return merged
            .Select(kv => new WatchStatRow(kv.Key, $"{kv.Value.Plays} plays", Math.Round(kv.Value.Hours, 1), kv.Value.Plays, null, null, null))
            .OrderByDescending(r => r.Hours)
            .Take(10)
            .ToList();
    }

    private async Task<IReadOnlyList<TracearrHistoryRow>> FetchHistoryAsync(
        string? serverId,
        DateTime startDate,
        DateTime endDate,
        int maxRows,
        CancellationToken ct)
    {
        await EnsureServerTypeMapAsync(ct);
        var results = new List<TracearrHistoryRow>();
        var page = 1;
        const int pageSize = 100;

        while (results.Count < maxRows)
        {
            var query = new List<string>
            {
                $"page={page}",
                $"pageSize={Math.Min(pageSize, maxRows - results.Count)}",
                $"startDate={startDate:yyyy-MM-dd}",
                $"endDate={endDate:yyyy-MM-dd}",
                "timezone=UTC"
            };
            if (!string.IsNullOrWhiteSpace(serverId))
                query.Add($"serverId={Uri.EscapeDataString(serverId)}");

            var doc = await GetJsonAsync($"/api/v1/public/history?{string.Join('&', query)}", ct);
            if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                break;

            var pageCount = 0;
            foreach (var row in data.EnumerateArray())
            {
                var mapped = MapHistoryRow(row);
                if (mapped is not null)
                {
                    results.Add(mapped);
                    pageCount++;
                }
            }

            if (pageCount < pageSize)
                break;

            page++;
        }

        return results;
    }

    private TracearrHistoryRow? MapHistoryRow(JsonElement row)
    {
        var started = ReadDate(row, "startedAt", "started_at");
        if (started is null)
            return null;

        var mediaType = NormalizeMediaType(ReadString(row, "mediaType", "media_type"));
        var mediaTitle = ReadString(row, "mediaTitle", "media_title", "title");
        if (string.IsNullOrWhiteSpace(mediaTitle))
            return null;

        var showTitle = ReadString(row, "showTitle", "grandparentTitle", "grandparent_title");
        var title = mediaType == "episode" && !string.IsNullOrWhiteSpace(showTitle) ? showTitle : mediaTitle;
        var episodeTitle = mediaType == "episode" ? mediaTitle : null;

        var (userId, userName, userThumb) = ReadUser(row);
        var serverType = ResolveServerType(row);
        var itemId = ExtractItemId(ReadString(row, "thumbPath", "thumb_path"));
        var durationMs = ReadLong(row, "durationMs", "duration_ms") ?? 0;
        var state = ReadString(row, "state");
        var sessionId = ReadString(row, "id");
        var transcode = ReadString(row, "videoDecision", "video_decision");
        if (string.IsNullOrWhiteSpace(transcode) && row.TryGetProperty("isTranscode", out var isTc))
        {
            if (isTc.ValueKind == JsonValueKind.True)
                transcode = "transcode";
            else if (isTc.ValueKind == JsonValueKind.False)
                transcode = "directplay";
        }

        if (string.IsNullOrWhiteSpace(userName))
            return null;

        return new TracearrHistoryRow(
            userName,
            title,
            showTitle,
            episodeTitle,
            mediaType,
            started.Value,
            (int)Math.Max(0, durationMs / 1000),
            serverType,
            ResolveImageUrl(ReadString(row, "posterUrl", "poster_url")),
            userId,
            ResolveImageUrl(userThumb),
            ReadString(row, "platform", "product"),
            itemId,
            SessionId: sessionId,
            State: state,
            Client: ReadString(row, "player", "device", "product"),
            TranscodeDecision: transcode,
            SeasonNumber: mediaType == "episode" ? ReadInt(row, "seasonNumber") : null,
            EpisodeNumber: mediaType == "episode" ? ReadInt(row, "episodeNumber") : null);
    }

    private static (string? Id, string Name, string? Thumb) ReadUser(JsonElement row)
    {
        if (row.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            var name = ReadString(user, "username", "displayName", "name");
            if (!string.IsNullOrWhiteSpace(name))
                return (ReadString(user, "id"), name, ReadString(user, "avatarUrl", "thumbUrl"));
        }

        var flat = ReadString(row, "username", "userName", "displayName");
        return (null, flat ?? "", null);
    }

    private string ResolveServerType(JsonElement row)
    {
        var explicitType = ReadString(row, "serverType", "server_type");
        if (!string.IsNullOrWhiteSpace(explicitType))
            return explicitType;

        if (row.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object)
        {
            var nested = ReadString(server, "type", "serverType");
            if (!string.IsNullOrWhiteSpace(nested))
                return nested;
        }

        var serverId = ReadString(row, "serverId", "server_id");
        if (!string.IsNullOrWhiteSpace(serverId)
            && _serverTypeById is not null
            && _serverTypeById.TryGetValue(serverId, out var mapped))
            return mapped;

        return "unknown";
    }

    private async Task EnsureServerTypeMapAsync(CancellationToken ct)
    {
        if (_serverTypeById is not null && DateTimeOffset.UtcNow - _serverTypeCachedAt < TimeSpan.FromMinutes(5))
            return;

        var servers = await GetServersAsync(ct);
        _serverTypeById = servers.ToDictionary(s => s.Id, s => s.Type, StringComparer.OrdinalIgnoreCase);
        _serverTypeCachedAt = DateTimeOffset.UtcNow;
    }

    private string? ResolveImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        return $"/api/thumbnail/tracearr?path={Uri.EscapeDataString(url)}";
    }

    private static readonly Regex ItemIdPattern = new(@"/(?:metadata|Items)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Tracearr's history endpoint doesn't expose a dedicated per-item id, but the thumb path
    // embeds one — Plex: /library/metadata/{ratingKey}/..., Emby/Jellyfin: /Items/{itemId}/...
    private static string? ExtractItemId(string? thumbPath)
    {
        if (string.IsNullOrWhiteSpace(thumbPath))
            return null;

        var match = ItemIdPattern.Match(thumbPath);
        return match.Success ? match.Groups[1].Value : null;
    }

    public async Task<IReadOnlyList<ActivityDrilldownPlay>> FetchDrilldownPlaysAsync(
        ActivityDrilldownRequest request,
        CancellationToken ct)
    {
        // Tracearr history exposes neither genre nor transcode-decision data in this path,
        // so these kinds always filter down to zero matches — skip the network round-trips entirely.
        if (request.Kind is ActivityDrilldownKind.Genre or ActivityDrilldownKind.Quality)
            return [];

        var (start, end) = WatchStatsPeriodHelper.RangeUtc(
            request.Period,
            request.CustomStartUtc,
            request.CustomEndUtc);
        var maxResults = 200;
        var maxPages = request.Period switch
        {
            WatchStatsPeriod.Today => 2,
            WatchStatsPeriod.Days7 => 4,
            WatchStatsPeriod.Days30 => 8,
            WatchStatsPeriod.All => 20,
            _ => 12
        };

        var matches = new List<ActivityDrilldownPlay>();
        var page = 1;
        const int pageSize = 100;
        const int batchSize = 4; // pages fetched concurrently per round-trip

        while (matches.Count < maxResults && page <= maxPages)
        {
            var pagesInBatch = Math.Min(batchSize, maxPages - page + 1);
            var pageNumbers = Enumerable.Range(page, pagesInBatch).ToList();
            var batches = await Task.WhenAll(
                pageNumbers.Select(p => FetchHistoryPageAsync(start, end, p, pageSize, ct)));

            var stop = false;
            foreach (var batch in batches)
            {
                if (batch.Count == 0)
                {
                    stop = true;
                    break;
                }

                IEnumerable<TracearrHistoryRow> filtered = batch;
                if (request.Kind == ActivityDrilldownKind.User)
                {
                    filtered = batch.Where(h =>
                        string.Equals(h.UserDisplayName, request.Key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(h.UserId, request.Key, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(request.Title)
                            && string.Equals(h.UserDisplayName, request.Title, StringComparison.OrdinalIgnoreCase)));
                }
                else if (request.Kind == ActivityDrilldownKind.Platform)
                {
                    var platform = !string.IsNullOrWhiteSpace(request.Key) ? request.Key : request.Title;
                    filtered = batch.Where(h =>
                        string.Equals(h.Platform, platform, StringComparison.OrdinalIgnoreCase));
                }
                else if (request.Kind == ActivityDrilldownKind.MediaType)
                {
                    var raw = (request.Key ?? request.Title ?? "").Trim().ToLowerInvariant();
                    var mediaType = raw switch
                    {
                        "movie" or "movies" => "movie",
                        "episode" or "episodes" or "tv" or "television" => "episode",
                        "music" or "track" or "tracks" or "audio" => "music",
                        _ => raw
                    };
                    filtered = batch.Where(h =>
                        string.Equals(h.MediaType, mediaType, StringComparison.OrdinalIgnoreCase));
                }
                else if (!string.IsNullOrWhiteSpace(request.Key)
                         && request.Key.StartsWith("tid:", StringComparison.OrdinalIgnoreCase))
                {
                    // Precise per-episode/movie lookup via the id embedded in its thumb path.
                    var itemId = request.Key[4..];
                    filtered = batch.Where(h =>
                        string.Equals(h.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    filtered = batch.Where(h =>
                        string.Equals(h.Title, request.Key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(h.EpisodeTitle, request.Key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(h.Title, request.Title, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(h.EpisodeTitle, request.Title, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(request.Source))
                {
                    filtered = filtered.Where(h =>
                        string.Equals(MapServerType(h.ServerType), request.Source, StringComparison.OrdinalIgnoreCase));
                }

                matches.AddRange(filtered
                    .Select(h => new ActivityDrilldownPlay(
                        h.Title,
                        h.EpisodeTitle,
                        h.UserDisplayName,
                        Math.Round(h.DurationSeconds / 3600.0, 2),
                        h.PlayedAtUtc,
                        h.PosterUrl,
                        MapServerType(h.ServerType),
                        h.MediaType,
                        null,
                        h.UserId,
                        h.Platform,
                        h.EpisodeTitle,
                        !string.IsNullOrWhiteSpace(h.ItemId) ? $"tid:{h.ItemId}" : null)));

                if (batch.Count < pageSize)
                {
                    stop = true;
                    break;
                }
            }

            page += pagesInBatch;

            if (stop)
                break;
        }

        return matches
            .OrderByDescending(p => p.PlayedAtUtc)
            .Take(maxResults)
            .ToList();
    }

    private async Task<IReadOnlyList<TracearrHistoryRow>> FetchHistoryPageAsync(
        DateTime startDate,
        DateTime endDate,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        await EnsureServerTypeMapAsync(ct);
        var query = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
            $"startDate={startDate:yyyy-MM-dd}",
            $"endDate={endDate:yyyy-MM-dd}",
            "timezone=UTC"
        };

        var doc = await GetJsonAsync($"/api/v1/public/history?{string.Join('&', query)}", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        return data.EnumerateArray()
            .Select(MapHistoryRow)
            .Where(r => r is not null)
            .Cast<TracearrHistoryRow>()
            .ToList();
    }

    private TracearrPlayEvent MapEvent(TracearrHistoryRow row, string sourceKey) => new(
        sourceKey,
        row.UserDisplayName,
        row.Title,
        row.SeriesTitle,
        row.MediaType,
        row.PlayedAtUtc,
        row.DurationSeconds,
        row.PosterUrl,
        row.Platform,
        row.UserThumbUrl,
        row.UserId,
        row.EpisodeTitle,
        row.ItemId);

    private static IReadOnlyList<WatchStatRow> BuildTopUsers(IReadOnlyList<TracearrPlayEvent> events, int limit) =>
        events
            .Where(e => !string.IsNullOrWhiteSpace(e.UserDisplayName)
                && !string.Equals(e.UserDisplayName, "Unknown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.UserDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                g.Select(e => e.UserThumbUrl).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                null,
                g.First().Source,
                DrilldownKey: g.Select(e => e.UserId).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? g.Key,
                DrilldownKind: ActivityDrilldownKind.User))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopMedia(IReadOnlyList<TracearrPlayEvent> events, string mediaType, int limit) =>
        events.Where(e => string.Equals(e.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                mediaType == "episode" ? g.Select(e => e.SeriesTitle).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) : null,
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                g.Select(e => e.PosterUrl).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                null,
                g.First().Source,
                DrilldownKey: g.Key,
                DrilldownKind: ActivityDrilldownKind.Media))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopPlatforms(IReadOnlyList<TracearrPlayEvent> events, int limit) =>
        events.GroupBy(e => e.Platform ?? "Unknown", StringComparer.OrdinalIgnoreCase)
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

    private static IReadOnlyList<WatchStatRow> BuildRecent(IReadOnlyList<TracearrPlayEvent> events, int limit) =>
        events
            .OrderByDescending(e => e.PlayedAtUtc)
            .GroupBy(RecentGroupKey)
            .Select(g => g.First())
            .Take(limit)
            .Select(e => new WatchStatRow(
                e.Title,
                e.EpisodeTitle ?? $"{e.UserDisplayName} · {WatchStatsSources.Label(e.Source)}",
                Math.Round(e.DurationSeconds / 3600.0, 1),
                1,
                e.PosterUrl,
                null,
                e.Source,
                DrilldownKey: e.Title,
                DrilldownKind: ActivityDrilldownKind.Media))
            .ToList();

    // Groups repeat watches of the same episode/movie into a single "recently watched" row,
    // keeping just the most recent play (events are pre-sorted newest-first before grouping).
    private static string RecentGroupKey(TracearrPlayEvent e) =>
        !string.IsNullOrWhiteSpace(e.ItemId)
            ? $"id:{e.Source}:{e.ItemId}"
            : $"t:{e.Source}:{e.Title}|{e.EpisodeTitle}";

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var url = $"{Tracearr.Url.TrimEnd('/')}{path}";
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Tracearr.ApiKey.Trim());
        return await http.SendAsync(request, ct);
    }

    private static string? MapTracearrPeriod(WatchStatsPeriod period) => period switch
    {
        WatchStatsPeriod.Days7 => "week",
        WatchStatsPeriod.Days30 => "month",
        WatchStatsPeriod.Year => "year",
        WatchStatsPeriod.All => "year",
        WatchStatsPeriod.Custom => "month",
        _ => null
    };

    private static string MapServerType(string? type) => type?.ToLowerInvariant() switch
    {
        "plex" => WatchStatsSources.Plex,
        "emby" => WatchStatsSources.Emby,
        "jellyfin" => WatchStatsSources.Jellyfin,
        _ => "unknown"
    };

    private static string NormalizeMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "movie" => "movie",
        "episode" => "episode",
        "track" => "music",
        "music" => "music",
        _ => "other"
    };

    private static string? ReadString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetRawText();
        }

        return null;
    }

    private static int? ReadInt(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
                return n;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static long? ReadLong(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
                return n;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n))
                return n;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                continue;
            if (DateTimeOffset.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }
}

public sealed record TracearrServerInfo(string Id, string Name, string Type);

public sealed record TracearrDashboardStats(
    int ActiveStreams,
    int TotalUsers,
    int TotalSessions,
    int RecentViolations);

public sealed record TracearrActivityData(
    IReadOnlyList<ActivityChartPoint> PlaysOverTime,
    IReadOnlyList<ActivityChartPoint> ByDayOfWeek,
    IReadOnlyList<ActivityChartPoint> ByHourOfDay,
    IReadOnlyList<ActivityChartPoint> Platforms,
    IReadOnlyList<ActivityChartPoint> Quality,
    ActivityQualityStats? QualityStats,
    IReadOnlyList<ActivityConcurrentPoint> ConcurrentStreams);

internal sealed record TracearrHistoryRow(
    string UserDisplayName,
    string Title,
    string? SeriesTitle,
    string? EpisodeTitle,
    string MediaType,
    DateTimeOffset PlayedAtUtc,
    int DurationSeconds,
    string ServerType,
    string? PosterUrl,
    string? UserId,
    string? UserThumbUrl,
    string? Platform,
    string? ItemId = null,
    string? SessionId = null,
    string? State = null,
    string? Client = null,
    string? TranscodeDecision = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);

internal sealed record TracearrPlayEvent(
    string Source,
    string UserDisplayName,
    string Title,
    string? SeriesTitle,
    string MediaType,
    DateTimeOffset PlayedAtUtc,
    int DurationSeconds,
    string? PosterUrl,
    string? Platform = null,
    string? UserThumbUrl = null,
    string? UserId = null,
    string? EpisodeTitle = null,
    string? ItemId = null);
