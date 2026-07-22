namespace ArrDash.Data.Entities;

/// <summary>Cached Sonarr/Radarr tag id → label (e.g. requester usernames).</summary>
public sealed class ArrTagEntity
{
    public long Id { get; set; }
    public string Source { get; set; } = "";
    public int TagId { get; set; }
    public string Label { get; set; } = "";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
