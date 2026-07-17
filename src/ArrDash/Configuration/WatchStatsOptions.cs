namespace ArrDash.Configuration;

public sealed class WatchStatsOptions
{
    public const string SectionName = "WatchStats";

    public int SyncIntervalMinutes { get; set; } = 20;
    public int BackfillDays { get; set; } = 90;
    public int RetentionDays { get; set; } = 365;
    public int TopListLimit { get; set; } = 10;
}
