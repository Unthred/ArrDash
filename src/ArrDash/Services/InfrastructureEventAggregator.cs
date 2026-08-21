using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Replays JSONL infrastructure events into correlated alert instances.
/// See docs/infrastructure-events.md for the schema contract.
/// </summary>
public static class InfrastructureEventAggregator
{
    internal static readonly TimeSpan LegacyStaleAfter = TimeSpan.FromHours(2);
    internal static readonly TimeSpan RecentlyClearedWindow = TimeSpan.FromHours(24);
    internal static readonly HashSet<InfrastructureEventSeverity> BadgeSeverities =
    [
        InfrastructureEventSeverity.Warning,
        InfrastructureEventSeverity.Alert,
        InfrastructureEventSeverity.Critical
    ];

    public static InfrastructureEventsSnapshot BuildSnapshot(
        IReadOnlyList<InfrastructureEventRecord> events,
        DateTimeOffset now)
    {
        var instances = new Dictionary<string, MutableInstance>(StringComparer.OrdinalIgnoreCase);

        foreach (var evt in events.OrderBy(e => e.At))
            ApplyEvent(instances, evt, now);

        var alerts = instances.Values
            .Select(v => v.ToAlert())
            .OrderByDescending(a => SeverityRank(a.Severity))
            .ThenByDescending(a => a.LastSeen)
            .ToList();

        var active = alerts
            .Where(a => a.State == InfrastructureAlertState.Active && BadgeSeverities.Contains(a.Severity))
            .ToList();

        var recentlyCleared = alerts
            .Where(a => a.State == InfrastructureAlertState.Cleared
                        && a.ClearedAt is not null
                        && now - a.ClearedAt.Value <= RecentlyClearedWindow
                        && BadgeSeverities.Contains(a.Severity))
            .ToList();

        var informational = events
            .Where(e => e.Lifecycle is InfrastructureEventLifecycle.Info
                        or InfrastructureEventLifecycle.Unknown
                        && !BadgeSeverities.Contains(e.Severity)
                        || e.Code.Contains("turbo", StringComparison.OrdinalIgnoreCase)
                        || e.Severity == InfrastructureEventSeverity.Info)
            .OrderByDescending(e => e.At)
            .Take(100)
            .ToList();

        return new InfrastructureEventsSnapshot(active, recentlyCleared, informational, now, null);
    }

    private static void ApplyEvent(
        Dictionary<string, MutableInstance> instances,
        InfrastructureEventRecord evt,
        DateTimeOffset now)
    {
        var key = string.IsNullOrWhiteSpace(evt.InstanceId)
            ? $"{evt.Source}:{evt.Code}"
            : evt.InstanceId;

        if (!instances.TryGetValue(key, out var inst))
        {
            inst = new MutableInstance(key, evt.Source, evt.Code);
            instances[key] = inst;
        }

        inst.Source = evt.Source;
        inst.Code = evt.Code;

        switch (evt.Lifecycle)
        {
            case InfrastructureEventLifecycle.Raised:
            case InfrastructureEventLifecycle.Updated:
                inst.HasLifecycle = true;
                inst.IsActive = true;
                inst.State = InfrastructureAlertState.Active;
                inst.Severity = evt.Severity;
                inst.Title = string.IsNullOrWhiteSpace(evt.Title) ? evt.Code : evt.Title;
                inst.Message = evt.Message;
                inst.Detail = evt.Detail;
                inst.FirstSeen = evt.FirstSeen ?? inst.FirstSeen ?? evt.At;
                inst.LastSeen = evt.LastSeen ?? evt.At;
                inst.OccurrenceCount = evt.Count ?? (inst.OccurrenceCount + 1);
                inst.ClearedAt = null;
                break;

            case InfrastructureEventLifecycle.Cleared:
                inst.HasLifecycle = true;
                inst.IsActive = false;
                inst.State = InfrastructureAlertState.Cleared;
                inst.LastSeen = evt.LastSeen ?? evt.At;
                inst.ClearedAt = evt.At;
                if (!string.IsNullOrWhiteSpace(evt.Message))
                    inst.Message = evt.Message;
                if (!string.IsNullOrWhiteSpace(evt.Detail))
                    inst.Detail = evt.Detail;
                break;

            case InfrastructureEventLifecycle.Info:
                // One-shot informational — do not change active warning state.
                break;

            default:
                // Legacy: active boolean + optional 2h stale fallback.
                if (evt.LegacyActive is not bool legacyActive)
                    break;

                inst.HasLifecycle = false;
                inst.Severity = evt.Severity;
                inst.Title = string.IsNullOrWhiteSpace(evt.Title) ? evt.Code : evt.Title;
                inst.Message = evt.Message;
                inst.Detail = evt.Detail;
                inst.FirstSeen ??= evt.FirstSeen ?? evt.At;
                inst.LastSeen = evt.LastSeen ?? evt.At;

                if (legacyActive && BadgeSeverities.Contains(evt.Severity))
                {
                    if (now - evt.At > LegacyStaleAfter)
                    {
                        inst.IsActive = false;
                        inst.State = InfrastructureAlertState.LegacyStale;
                    }
                    else
                    {
                        inst.IsActive = true;
                        inst.State = InfrastructureAlertState.Active;
                        inst.OccurrenceCount++;
                    }
                }
                else if (!legacyActive)
                {
                    inst.IsActive = false;
                    inst.State = InfrastructureAlertState.Cleared;
                    inst.ClearedAt = evt.At;
                }
                break;
        }
    }

    internal static bool TryParseLine(string line, out InfrastructureEventRecord record)
    {
        record = default!;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ts", out var tsEl))
                return false;

            if (!DateTimeOffset.TryParse(tsEl.GetString(), out var at))
                at = DateTimeOffset.UtcNow;

            var source = GetString(root, "source") ?? "unknown";
            var code = GetString(root, "code") ?? "unknown";
            var severity = MapSeverity(GetString(root, "severity"));
            var lifecycle = MapLifecycle(GetString(root, "lifecycle"));
            var title = GetString(root, "title") ?? code;
            var message = GetString(root, "message") ?? "";
            var detail = GetString(root, "detail");
            var instanceId = GetString(root, "instance") ?? $"{source}:{code}";

            int? count = root.TryGetProperty("count", out var cEl) && cEl.TryGetInt32(out var c) ? c : null;
            DateTimeOffset? firstSeen = ParseOptionalTs(root, "firstSeen");
            DateTimeOffset? lastSeen = ParseOptionalTs(root, "lastSeen");
            bool? legacyActive = root.TryGetProperty("active", out var aEl) && aEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? aEl.GetBoolean()
                : null;

            record = new InfrastructureEventRecord(
                at, source, code, severity, lifecycle, title, message, detail,
                instanceId, count, firstSeen, lastSeen, legacyActive);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? el.GetString() : null;

    private static DateTimeOffset? ParseOptionalTs(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return null;
        return DateTimeOffset.TryParse(el.GetString(), out var ts) ? ts : null;
    }

    internal static InfrastructureEventSeverity MapSeverity(string? severity) =>
        severity?.ToLowerInvariant() switch
        {
            "critical" => InfrastructureEventSeverity.Critical,
            "alert" => InfrastructureEventSeverity.Alert,
            "warning" => InfrastructureEventSeverity.Warning,
            _ => InfrastructureEventSeverity.Info
        };

    internal static InfrastructureEventLifecycle MapLifecycle(string? lifecycle) =>
        lifecycle?.ToLowerInvariant() switch
        {
            "raised" => InfrastructureEventLifecycle.Raised,
            "updated" => InfrastructureEventLifecycle.Updated,
            "cleared" => InfrastructureEventLifecycle.Cleared,
            "info" => InfrastructureEventLifecycle.Info,
            _ => InfrastructureEventLifecycle.Unknown
        };

    private static int SeverityRank(InfrastructureEventSeverity severity) => severity switch
    {
        InfrastructureEventSeverity.Critical => 4,
        InfrastructureEventSeverity.Alert => 3,
        InfrastructureEventSeverity.Warning => 2,
        _ => 1
    };

    private sealed class MutableInstance(string instanceId, string source, string code)
    {
        public string InstanceId { get; } = instanceId;
        public string Source { get; set; } = source;
        public string Code { get; set; } = code;
        public bool HasLifecycle;
        public bool IsActive;
        public InfrastructureAlertState State = InfrastructureAlertState.Active;
        public InfrastructureEventSeverity Severity = InfrastructureEventSeverity.Info;
        public string Title = code;
        public string Message = "";
        public string? Detail;
        public DateTimeOffset? FirstSeen;
        public DateTimeOffset LastSeen;
        public int OccurrenceCount;
        public DateTimeOffset? ClearedAt;

        public InfrastructureAlert ToAlert() => new(
            InstanceId, Source, Code, Severity, Title, Message, Detail,
            State, FirstSeen ?? LastSeen, LastSeen, Math.Max(OccurrenceCount, 1), ClearedAt);
    }
}
