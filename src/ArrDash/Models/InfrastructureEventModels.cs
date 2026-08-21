namespace ArrDash.Models;

public enum InfrastructureEventSeverity
{
    Info,
    Warning,
    Alert,
    Critical
}

/// <summary>Lifecycle state from JSONL producers. <see cref="Unknown"/> = legacy/malformed.</summary>
public enum InfrastructureEventLifecycle
{
    Unknown,
    Raised,
    Updated,
    Cleared,
    Info
}

public enum InfrastructureAlertState
{
    Active,
    Cleared,
    LegacyStale
}

/// <summary>One parsed JSONL record (schema v1).</summary>
public sealed record InfrastructureEventRecord(
    DateTimeOffset At,
    string Source,
    string Code,
    InfrastructureEventSeverity Severity,
    InfrastructureEventLifecycle Lifecycle,
    string Title,
    string Message,
    string? Detail,
    string InstanceId,
    int? Count,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    bool? LegacyActive);

/// <summary>Aggregated view of one correlated warning instance.</summary>
public sealed record InfrastructureAlert(
    string InstanceId,
    string Source,
    string Code,
    InfrastructureEventSeverity Severity,
    string Title,
    string Message,
    string? Detail,
    InfrastructureAlertState State,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int OccurrenceCount,
    DateTimeOffset? ClearedAt);

public sealed record InfrastructureEventsSnapshot(
    IReadOnlyList<InfrastructureAlert> Active,
    IReadOnlyList<InfrastructureAlert> RecentlyCleared,
    IReadOnlyList<InfrastructureEventRecord> InformationalHistory,
    DateTimeOffset ReadAt,
    string? ReadError);
