namespace ArrDash.Data.Entities;

/// <summary>
/// Idempotent record of a Trakt→media-server "mark watched" write (additive only).
/// </summary>
public sealed class ServerWatchedLinkEntity
{
    public long Id { get; set; }
    public string AccountId { get; set; } = "";
    /// <summary>emby | plex | jellyfin</summary>
    public string Server { get; set; } = "";
    public string ServerUserId { get; set; } = "";
    public string ServerItemId { get; set; } = "";
    public string CanonicalMediaKey { get; set; } = "";
    public string MediaType { get; set; } = "";
    public DateTimeOffset LinkedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
