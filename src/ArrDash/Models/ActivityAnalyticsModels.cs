namespace ArrDash.Models;

public sealed record ActivityChartPoint(string Label, double Value, string? Series = null);

public sealed record ActivityMediaSeries(string Name, IReadOnlyList<double> Values);

public sealed record ActivityMediaMix(
    IReadOnlyList<string> Categories,
    IReadOnlyList<ActivityMediaSeries> Series);

public sealed record ActivityDataSourceStatus(
    string Key,
    string Label,
    bool Configured,
    string? Error);

public sealed record ActivityQualityStats(
    int DirectPlay,
    int DirectStream,
    int Transcode,
    int Total,
    int DirectPlayPercent,
    int DirectStreamPercent,
    int TranscodePercent);

public sealed record ActivityConcurrentPoint(
    string Label,
    double Total,
    double Direct,
    double DirectStream,
    double Transcode);

public sealed record ActivityTodayStats(
    int TodayPlays,
    double WatchTimeHours,
    int ActiveUsersToday,
    int AlertsLast24h);

public sealed record ActivityAnalyticsSnapshot(
    WatchStatsPeriod Period,
    WatchStatsSourceFilter Filter,
    WatchStatsSummary Summary,
    int ActiveStreams,
    int TotalUsers,
    int TotalSessions,
    IReadOnlyList<ActivityChartPoint> PlaysOverTime,
    ActivityMediaMix? MediaMix,
    IReadOnlyList<ActivityChartPoint> ByDayOfWeek,
    IReadOnlyList<ActivityChartPoint> ByHourOfDay,
    IReadOnlyList<ActivityChartPoint> Platforms,
    IReadOnlyList<ActivityChartPoint> Quality,
    ActivityQualityStats? QualityStats,
    IReadOnlyList<ActivityConcurrentPoint> ConcurrentStreams,
    ActivityTodayStats? TodayStats,
    WatchStatsSourceSnapshot? Leaderboards,
    IReadOnlyList<ActivityDataSourceStatus> Sources,
    DateTimeOffset UpdatedAt);
