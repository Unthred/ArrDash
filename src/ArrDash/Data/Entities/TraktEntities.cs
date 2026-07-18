namespace ArrDash.Data.Entities;

public sealed class TraktAccountEntity
{
    public string Id { get; set; } = "";
    public string TraktUsername { get; set; } = "";
    public string CanonicalUserName { get; set; } = "";
    public string EncryptedAccessToken { get; set; } = "";
    public string EncryptedRefreshToken { get; set; } = "";
    public DateTimeOffset TokenExpiresAtUtc { get; set; }
    public bool SyncMovies { get; set; } = true;
    public bool SyncEpisodes { get; set; } = true;
    public bool ImportToWarehouse { get; set; } = true;
    public bool PushToTrakt { get; set; }
    public bool MarkPlexWatched { get; set; }
    public bool MarkEmbyWatched { get; set; }
    public bool MarkJellyfinWatched { get; set; }
    /// <summary>JSON: [{ "source":"emby", "userName":"Squiggley", "userId":"..." }]</summary>
    public string MappedUsersJson { get; set; } = "[]";
    public DateTimeOffset? HistoryStartUtc { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public DateTimeOffset? LastPreviewAtUtc { get; set; }
    public string? LastError { get; set; }
    public string? LastPreviewJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TraktHistoryLinkEntity
{
    public long Id { get; set; }
    public string AccountId { get; set; } = "";
    public long? PlayEventId { get; set; }
    public long TraktHistoryId { get; set; }
    public string Direction { get; set; } = ""; // pull | push
    public string CanonicalMediaKey { get; set; } = "";
    public DateTimeOffset LinkedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MediaIdentityEntity
{
    public string CanonicalMediaKey { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SeriesTitle { get; set; }
    public int? Year { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? ImdbId { get; set; }
    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public int? TraktId { get; set; }
    public int? RuntimeSeconds { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
