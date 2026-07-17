namespace ArrDash.Data.Entities;

public sealed class SyncCursorEntity
{
    public string Source { get; set; } = "";
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public string? LastExternalId { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
}
