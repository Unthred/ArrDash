namespace ArrDash.Data.Entities;

public sealed class PlayEventEntity
{
    public long Id { get; set; }
    public string Source { get; set; } = "";
    public string ExternalPlayId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string? UserExternalId { get; set; }
    public string Title { get; set; } = "";
    public string? SeriesTitle { get; set; }
    public string MediaType { get; set; } = "";
    public string GenresJson { get; set; } = "[]";
    public string? Client { get; set; }
    public string? Platform { get; set; }
    public DateTime PlayedAtUtc { get; set; }
    public int DurationSeconds { get; set; }
    public string? ExternalItemId { get; set; }
    /// <summary>The played item's own title (episode name); Title holds the series for episodes.</summary>
    public string? ItemTitle { get; set; }
    public string? GrandparentExternalId { get; set; }
    public string? ThumbPath { get; set; }
    public string? TranscodeDecision { get; set; }
    public string? LibraryName { get; set; }
    public string? LibraryExternalId { get; set; }
    public double? ProgressPercent { get; set; }

    /// <summary>Origin of the play event (plex, emby, jellyfin, trakt).</summary>
    public string? Origin { get; set; }
    public int? Year { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? ImdbId { get; set; }
    public int? TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public int? TraktId { get; set; }
    public bool WasCompleted { get; set; } = true;
    public bool DurationIsEstimated { get; set; }
    public string? CanonicalMediaKey { get; set; }
}
