namespace ArrDash.Data.Entities;

/// <summary>
/// Idempotent record of a media-server library watched item pushed to Trakt history or collection.
/// </summary>
public sealed class TraktLibrarySyncLinkEntity
{
    public long Id { get; set; }
    public string AccountId { get; set; } = "";
    /// <summary>emby | plex | jellyfin</summary>
    public string Server { get; set; } = "";
    public string ServerItemId { get; set; } = "";
    /// <summary>history | collection</summary>
    public string Direction { get; set; } = "";
    public string CanonicalMediaKey { get; set; } = "";
    public string MediaType { get; set; } = "";
    public DateTimeOffset LinkedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
