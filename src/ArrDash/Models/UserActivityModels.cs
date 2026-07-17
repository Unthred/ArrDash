namespace ArrDash.Models;

public sealed record TracearrUserInfo(
    string Id,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    string? ServerName,
    string? ServerId,
    int SessionCount,
    int TrustScore,
    int TotalViolations,
    DateTimeOffset? LastActivityAt);

public sealed record UserActivityRow(
    int Rank,
    string Username,
    string? DisplayName,
    string? AvatarUrl,
    double Hours,
    int Plays,
    double SharePercent,
    IReadOnlyList<string>? SourceBreakdown,
    string DrilldownKey,
    int? TrustScore,
    int? SessionCount,
    DateTimeOffset? LastActivityAt,
    string? ServerName);

public sealed record UserActivitySnapshot(
    WatchStatsPeriod Period,
    WatchStatsSourceFilter Filter,
    double TotalHours,
    int TotalPlays,
    int UserCount,
    double AverageHoursPerUser,
    UserActivityRow? Leader,
    IReadOnlyList<UserActivityRow> Users,
    IReadOnlyList<ActivityChartPoint> HoursByUser,
    DateTimeOffset UpdatedAt);
