using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

public static class ArrServiceDetailParser
{
    public static ServiceDetail BuildArrDetail(
        string key,
        string name,
        string? serviceUrl,
        string? version,
        bool online,
        string? connectionError,
        JsonElement? health,
        JsonElement? commands,
        JsonElement? queueStatus,
        JsonElement? queue,
        JsonElement? history,
        ServiceWorkload? workload)
    {
        var problems = ParseHealthProblems(health);
        var queueStates = BuildQueueBreakdown(queue);
        var queueTotal = ReadQueueTotalCount(queueStatus);
        var queueHasErrors = ReadQueueFlag(queueStatus, "errors");
        var queueHasWarnings = ReadQueueFlag(queueStatus, "warnings");

        if (queueHasErrors)
            problems.Add(new ServiceProblem("error", "Download queue reports errors"));
        if (queueHasWarnings)
            problems.Add(new ServiceProblem("warning", "Download queue reports warnings"));

        var blockedCount = queueStates
            .FirstOrDefault(s => string.Equals(s.State, "importBlocked", StringComparison.OrdinalIgnoreCase))
            ?.Count ?? 0;
        if (blockedCount > 0)
        {
            problems.Add(new ServiceProblem("warning",
                $"{blockedCount} import{(blockedCount == 1 ? "" : "s")} blocked — downloads finished but not imported"));
        }

        var attention = DetermineAttention(online, problems, workload, blockedCount > 0);

        return new ServiceDetail(
            key,
            name,
            Configured: true,
            online,
            version,
            serviceUrl,
            DateTimeOffset.UtcNow,
            attention,
            AttentionLabelFor(attention),
            workload,
            problems,
            queueStates,
            queueTotal,
            queueHasErrors,
            queueHasWarnings,
            ParseCommands(commands),
            ParseRecentHistory(history),
            [],
            connectionError);
    }

    public static ServiceDetail BuildStreamingDetail(
        string key,
        string name,
        string? serviceUrl,
        bool configured,
        bool online,
        string? connectionError,
        IReadOnlyList<ActiveSession> sessions,
        ServiceWorkload? workload)
    {
        if (!configured)
            return NotConfigured(key, name);

        var sessionSummaries = sessions.Select(s => new ServiceSessionSummary(
            s.Title,
            s.Subtitle,
            s.BandwidthKbps ?? s.BitrateKbps,
            s.IsLocal)).ToList();

        var problems = new List<ServiceProblem>();
        if (!online && !string.IsNullOrWhiteSpace(connectionError))
            problems.Add(new ServiceProblem("error", connectionError));

        var attention = !online
            ? ServiceAttentionLevel.Offline
            : workload is { Kind: not ServiceWorkloadKind.None }
                ? ServiceAttentionLevel.Busy
                : sessions.Count > 0
                    ? ServiceAttentionLevel.Busy
                    : ServiceAttentionLevel.Healthy;

        return new ServiceDetail(
            key,
            name,
            true,
            online,
            null,
            serviceUrl,
            DateTimeOffset.UtcNow,
            attention,
            AttentionLabelFor(attention),
            workload,
            problems,
            [],
            0,
            false,
            false,
            [],
            [],
            sessionSummaries,
            connectionError);
    }

    public static ServiceDetail BuildSimpleDetail(
        string key,
        string name,
        string? serviceUrl,
        bool configured,
        bool online,
        string? version,
        string? connectionError,
        ServiceWorkload? workload,
        IReadOnlyList<ServiceRecentActivity>? recentActivity = null,
        IReadOnlyList<ServiceProblem>? additionalProblems = null)
    {
        if (!configured)
            return NotConfigured(key, name);

        var problems = new List<ServiceProblem>();
        if (!online && !string.IsNullOrWhiteSpace(connectionError))
            problems.Add(new ServiceProblem("error", connectionError));
        if (additionalProblems is not null)
            problems.AddRange(additionalProblems);

        var attention = DetermineAttention(online, problems, workload, blockedQueue: false);

        return new ServiceDetail(
            key,
            name,
            true,
            online,
            version,
            serviceUrl,
            DateTimeOffset.UtcNow,
            attention,
            AttentionLabelFor(attention),
            workload,
            problems,
            [],
            0,
            false,
            false,
            [],
            recentActivity ?? [],
            [],
            connectionError);
    }

    public static ServiceDetail NotConfigured(string key, string name) =>
        new(
            key,
            name,
            false,
            false,
            null,
            null,
            DateTimeOffset.UtcNow,
            ServiceAttentionLevel.NotConfigured,
            AttentionLabelFor(ServiceAttentionLevel.NotConfigured),
            null,
            [new ServiceProblem("info", "Not configured — add URL and API key in Settings")],
            [],
            0,
            false,
            false,
            [],
            [],
            [],
            null);

    public static ServiceAttentionLevel DetermineAttention(
        bool online,
        IReadOnlyList<ServiceProblem> problems,
        ServiceWorkload? workload,
        bool blockedQueue)
    {
        if (!online)
            return ServiceAttentionLevel.Offline;

        if (problems.Any(p => string.Equals(p.Severity, "error", StringComparison.OrdinalIgnoreCase))
            || blockedQueue)
            return ServiceAttentionLevel.NeedsAttention;

        if (problems.Any(p => string.Equals(p.Severity, "warning", StringComparison.OrdinalIgnoreCase)))
            return ServiceAttentionLevel.NeedsAttention;

        if (workload is { Kind: not ServiceWorkloadKind.None })
            return ServiceAttentionLevel.Busy;

        return ServiceAttentionLevel.Healthy;
    }

    public static string AttentionLabelFor(ServiceAttentionLevel level) => level switch
    {
        ServiceAttentionLevel.NotConfigured => "Not configured",
        ServiceAttentionLevel.Offline => "Offline",
        ServiceAttentionLevel.Healthy => "Healthy",
        ServiceAttentionLevel.Busy => "Busy",
        ServiceAttentionLevel.NeedsAttention => "Needs attention",
        _ => "Unknown"
    };

    public static List<ServiceProblem> ParseHealthProblems(JsonElement? health)
    {
        var problems = new List<ServiceProblem>();
        if (health is not { } root)
            return problems;

        var array = root.ValueKind == JsonValueKind.Array ? root : default;
        if (array.ValueKind != JsonValueKind.Array)
            return problems;

        foreach (var item in array.EnumerateArray())
        {
            var message = item.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(message))
                continue;

            var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "warning";
            var wiki = item.TryGetProperty("wikiUrl", out var wikiEl) ? wikiEl.GetString() : null;
            var severity = string.Equals(type, "error", StringComparison.OrdinalIgnoreCase) ? "error" : "warning";
            problems.Add(new ServiceProblem(severity, message, wiki));
        }

        return problems;
    }

    public static IReadOnlyList<ServiceQueueStateCount> BuildQueueBreakdown(JsonElement? queue)
    {
        if (queue is not { } root)
            return [];

        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array
                ? records
                : default;

        if (array.ValueKind != JsonValueKind.Array)
            return [];

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in array.EnumerateArray())
        {
            var state = item.TryGetProperty("trackedDownloadState", out var tracked)
                ? tracked.GetString()
                : item.TryGetProperty("status", out var statusEl)
                    ? statusEl.GetString()
                    : null;

            state = string.IsNullOrWhiteSpace(state) ? "unknown" : state;
            counts[state] = counts.GetValueOrDefault(state) + 1;
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new ServiceQueueStateCount(kv.Key, kv.Value))
            .ToList();
    }

    public static IReadOnlyList<ServiceCommandInfo> ParseCommands(JsonElement? commands, int limit = 10)
    {
        if (commands is not { } root)
            return [];

        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array
                ? records
                : default;

        if (array.ValueKind != JsonValueKind.Array)
            return [];

        var rows = new List<(int Order, ServiceCommandInfo Row)>();
        foreach (var command in array.EnumerateArray())
        {
            var name = command.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var status = command.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(status))
                continue;

            DateTimeOffset? started = null;
            if (command.TryGetProperty("startedOn", out var startedEl) &&
                DateTimeOffset.TryParse(startedEl.GetString(), out var parsed))
                started = parsed;

            var order = string.Equals(status, "started", StringComparison.OrdinalIgnoreCase) ? 0
                : string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase) ? 1
                : 2;

            rows.Add((order, new ServiceCommandInfo(name, status, started)));
        }

        return rows
            .OrderBy(r => r.Order)
            .ThenByDescending(r => r.Row.StartedAt)
            .Take(limit)
            .Select(r => r.Row)
            .ToList();
    }

    public static IReadOnlyList<ServiceRecentActivity> ParseRecentHistory(JsonElement? history, int limit = 8)
    {
        if (history is not { } root)
            return [];

        var array = root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array
            ? records
            : root.ValueKind == JsonValueKind.Array ? root : default;

        if (array.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<ServiceRecentActivity>();
        foreach (var rec in array.EnumerateArray())
        {
            var title = rec.TryGetProperty("sourceTitle", out var titleEl) ? titleEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var eventType = rec.TryGetProperty("eventType", out var evEl) ? evEl.GetString() : "event";
            var date = rec.TryGetProperty("date", out var dateEl) && DateTimeOffset.TryParse(dateEl.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            items.Add(new ServiceRecentActivity(HistoryTitleFormatter.Format(rec), HumanizeEvent(eventType!), date));
            if (items.Count >= limit)
                break;
        }

        return items;
    }

    public static int ReadQueueTotalCount(JsonElement? queueStatus)
    {
        if (queueStatus is not { } status)
            return 0;

        if (status.TryGetProperty("totalCount", out var totalEl) && totalEl.TryGetInt32(out var total))
            return total;

        if (status.TryGetProperty("count", out var countEl) && countEl.TryGetInt32(out var count))
            return count;

        return 0;
    }

    public static int CountImportBlocked(JsonElement? queue)
    {
        if (queue is null)
            return 0;

        return BuildQueueBreakdown(queue)
            .FirstOrDefault(s => string.Equals(s.State, "importBlocked", StringComparison.OrdinalIgnoreCase))
            ?.Count ?? 0;
    }

    public static ServiceAttentionLevel DetermineAttentionFromSnapshot(
        bool online,
        JsonElement? health,
        JsonElement? queueStatus,
        JsonElement? queue,
        ServiceWorkload? workload)
    {
        if (!online)
            return ServiceAttentionLevel.Offline;

        var problems = ParseHealthProblems(health);
        if (ReadQueueFlag(queueStatus, "errors"))
            problems.Add(new ServiceProblem("error", "Download queue reports errors"));
        if (ReadQueueFlag(queueStatus, "warnings"))
            problems.Add(new ServiceProblem("warning", "Download queue reports warnings"));

        var blockedCount = CountImportBlocked(queue);
        return DetermineAttention(true, problems, workload, blockedCount > 0);
    }

    private static bool ReadQueueFlag(JsonElement? queueStatus, string property)
    {
        if (queueStatus is not { } status)
            return false;

        return status.TryGetProperty(property, out var el) &&
               el.ValueKind == JsonValueKind.True;
    }

    private static string HumanizeEvent(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return "Event";

        return eventType switch
        {
            "grabbed" => "Grabbed",
            "downloadFolderImported" => "Imported",
            "downloadFailed" => "Download failed",
            "bookFileDeleted" => "File deleted",
            _ => char.ToUpper(eventType[0]) + eventType[1..]
        };
    }
}
