namespace ArrDash.Data.Entities;

public sealed class MediaInventoryItemEntity
{
    public long Id { get; set; }
    public string Source { get; set; } = "";
    public int SourceItemId { get; set; }
    public string MediaType { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string? TitleSlug { get; set; }
    public string? ImdbId { get; set; }
    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public long SizeOnDiskBytes { get; set; }
    public int? FileCount { get; set; }
    public bool Monitored { get; set; }
    public bool HasFile { get; set; }
    public bool NeverDelete { get; set; }
    public bool MarkedForDeletion { get; set; }
    /// <summary>Sonarr series status: continuing, ended, upcoming, deleted.</summary>
    public string? SeriesStatus { get; set; }
    public double? Rating { get; set; }
    public DateTimeOffset? AddedUtc { get; set; }
    public string TagsJson { get; set; } = "[]";
    public DateTimeOffset LastSeenUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
