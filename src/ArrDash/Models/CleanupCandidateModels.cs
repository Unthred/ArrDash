namespace ArrDash.Models;

public sealed record MovieInventoryDto(
    int Id,
    string Title,
    int? Year,
    string? Slug,
    string? ImdbId,
    int? TmdbId,
    long SizeOnDiskBytes,
    bool Monitored,
    bool HasFile,
    DateTimeOffset? AddedUtc,
    IReadOnlyList<int> TagIds,
    double? Rating);

public sealed record SeriesInventoryDto(
    int Id,
    string Title,
    int? Year,
    string? Slug,
    string? ImdbId,
    int? TvdbId,
    long SizeOnDiskBytes,
    int? EpisodeFileCount,
    bool Monitored,
    bool HasFile,
    DateTimeOffset? AddedUtc,
    IReadOnlyList<int> TagIds,
    double? Rating,
    string? SeriesStatus);

public sealed record ArrTagDto(int Id, string Label);

public enum CleanupReason
{
    NeverWatched,
    WatchedLongAgo,
    Largest
}

public sealed record MediaInventorySyncStatus(
    bool IsRunning,
    string CurrentMessage,
    DateTimeOffset? LastSyncAt);

public sealed record CleanupCandidateItem(
    string Source,
    int SourceItemId,
    string MediaType,
    string Title,
    int? Year,
    long SizeBytes,
    DateTimeOffset? AddedUtc,
    DateTimeOffset? LastWatchedUtc,
    IReadOnlyList<string> WatchedBy,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CleanupReason> Reasons,
    string? ExternalUrl,
    string? ImdbId,
    bool NeverDelete,
    bool MarkedForDeletion,
    double? Rating,
    string? SeriesStatus);
