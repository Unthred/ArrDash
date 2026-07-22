namespace ArrDash.Configuration;

public sealed class CleanupCandidatesOptions
{
    public const string SectionName = "CleanupCandidates";

    public int SyncIntervalMinutes { get; set; } = 60;
    public int StaleThresholdMonths { get; set; } = 12;
}
