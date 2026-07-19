namespace ArrDash.Models;

public enum WatchStatsPeriod
{
    Today,
    Days7,
    Days30,
    Year,
    All,
    Custom
}

[Flags]
public enum WatchStatsSourceFilter
{
    None = 0,
    Plex = 1 << 0,
    Emby = 1 << 1,
    Jellyfin = 1 << 2,
    Trakt = 1 << 3,
    /// <summary>All known sources (API / legacy “Combined”).</summary>
    Combined = Plex | Emby | Jellyfin | Trakt
}

public static class WatchStatsSourceFilters
{
    public static bool IsSingleSource(WatchStatsSourceFilter filter) =>
        System.Numerics.BitOperations.PopCount((uint)filter) == 1;

    public static bool ShouldCollapse(WatchStatsSourceFilter filter) =>
        !IsSingleSource(filter);

    public static IReadOnlyList<string> ToSourceKeys(WatchStatsSourceFilter filter)
    {
        var keys = new List<string>(4);
        if (filter.HasFlag(WatchStatsSourceFilter.Plex))
            keys.Add(WatchStatsSources.Plex);
        if (filter.HasFlag(WatchStatsSourceFilter.Emby))
            keys.Add(WatchStatsSources.Emby);
        if (filter.HasFlag(WatchStatsSourceFilter.Jellyfin))
            keys.Add(WatchStatsSources.Jellyfin);
        if (filter.HasFlag(WatchStatsSourceFilter.Trakt))
            keys.Add(WatchStatsSources.Trakt);
        return keys;
    }

    public static WatchStatsSourceFilter FromSourceKey(string? key) =>
        key?.ToLowerInvariant() switch
        {
            WatchStatsSources.Plex => WatchStatsSourceFilter.Plex,
            WatchStatsSources.Emby => WatchStatsSourceFilter.Emby,
            WatchStatsSources.Jellyfin => WatchStatsSourceFilter.Jellyfin,
            WatchStatsSources.Trakt => WatchStatsSourceFilter.Trakt,
            _ => WatchStatsSourceFilter.None
        };

    public static WatchStatsSourceFilter FromConfigured(IEnumerable<string> configured)
    {
        var filter = WatchStatsSourceFilter.None;
        foreach (var key in configured)
            filter |= FromSourceKey(key);
        return filter;
    }

    public static WatchStatsSourceFilter RestrictToAvailable(
        WatchStatsSourceFilter selected,
        WatchStatsSourceFilter available)
    {
        var next = selected & available;
        return next == WatchStatsSourceFilter.None ? available : next;
    }

    public static string Label(WatchStatsSourceFilter filter) => filter switch
    {
        WatchStatsSourceFilter.Plex => "Plex",
        WatchStatsSourceFilter.Emby => "Emby",
        WatchStatsSourceFilter.Jellyfin => "Jellyfin",
        WatchStatsSourceFilter.Trakt => "Trakt",
        WatchStatsSourceFilter.Combined => "All",
        WatchStatsSourceFilter.None => "None",
        _ => string.Join(" + ", ToSourceKeys(filter).Select(WatchStatsSources.Label))
    };

    public static string CacheToken(WatchStatsSourceFilter filter) =>
        IsSingleSource(filter) || filter == WatchStatsSourceFilter.Combined
            ? filter.ToString()
            : string.Join("+", ToSourceKeys(filter).OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>
    /// Drilldown source token: null = all/collapse, single key, or comma-separated multi.
    /// </summary>
    public static string? ToDrilldownSource(WatchStatsSourceFilter filter)
    {
        var keys = ToSourceKeys(filter);
        if (keys.Count == 0)
            return null;
        if (keys.Count == 1)
            return keys[0];
        return string.Join(',', keys);
    }
}

public sealed record WatchStatsRange(
    WatchStatsPeriod Period,
    DateTime? CustomStartUtc = null,
    DateTime? CustomEndUtc = null)
{
    public static WatchStatsRange Year { get; } = new(WatchStatsPeriod.Year);

    public (DateTime StartUtc, DateTime EndUtc) Bounds() =>
        WatchStatsPeriodHelper.RangeUtc(Period, CustomStartUtc, CustomEndUtc);

    public bool IsQueryable =>
        Period != WatchStatsPeriod.Custom
        || (CustomStartUtc.HasValue && CustomEndUtc.HasValue && CustomStartUtc <= CustomEndUtc);
}

public static class WatchStatsPeriodHelper
{
    public const int AllLookbackDays = 3650;

    public static WatchStatsPeriod Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "today" => WatchStatsPeriod.Today,
        "week" or "7d" or "days7" => WatchStatsPeriod.Days7,
        "month" or "30d" or "days30" => WatchStatsPeriod.Days30,
        "year" or "1y" => WatchStatsPeriod.Year,
        "all" => WatchStatsPeriod.All,
        "custom" => WatchStatsPeriod.Custom,
        _ => Enum.TryParse<WatchStatsPeriod>(value, true, out var parsed) ? parsed : WatchStatsPeriod.Year
    };

    public static int ToDays(WatchStatsPeriod period) => period switch
    {
        WatchStatsPeriod.Today => 1,
        WatchStatsPeriod.Days7 => 7,
        WatchStatsPeriod.Days30 => 30,
        WatchStatsPeriod.Year => 365,
        WatchStatsPeriod.All => AllLookbackDays,
        WatchStatsPeriod.Custom => 30,
        _ => 7
    };

    public static int ToDays(WatchStatsRange range) =>
        range.Period == WatchStatsPeriod.Custom && range.CustomStartUtc.HasValue && range.CustomEndUtc.HasValue
            ? Math.Clamp((int)(range.CustomEndUtc.Value.Date - range.CustomStartUtc.Value.Date).TotalDays + 1, 1, AllLookbackDays)
            : ToDays(range.Period);

    public static (DateTime StartUtc, DateTime EndUtc) RangeUtc(
        WatchStatsPeriod period,
        DateTime? customStart = null,
        DateTime? customEnd = null)
    {
        var end = DateTime.UtcNow.Date;
        return period switch
        {
            WatchStatsPeriod.Today => (end, end),
            WatchStatsPeriod.Days7 => (end.AddDays(-6), end),
            WatchStatsPeriod.Days30 => (end.AddDays(-29), end),
            WatchStatsPeriod.Year => (end.AddDays(-364), end),
            WatchStatsPeriod.All => (end.AddDays(-AllLookbackDays), end),
            WatchStatsPeriod.Custom when customStart.HasValue && customEnd.HasValue =>
                (customStart.Value.Date, customEnd.Value.Date),
            _ => (end.AddDays(-6), end)
        };
    }

    public static DateTime CutoffUtc(WatchStatsPeriod period, DateTime? customStart = null, DateTime? customEnd = null) =>
        period == WatchStatsPeriod.Custom && customStart.HasValue
            ? customStart.Value.Date
            : DateTime.UtcNow.AddDays(-ToDays(period));

    public static DateTime CutoffUtc(WatchStatsRange range) =>
        CutoffUtc(range.Period, range.CustomStartUtc, range.CustomEndUtc);

    public static string Label(WatchStatsPeriod period, DateTime? customStart = null, DateTime? customEnd = null) => period switch
    {
        WatchStatsPeriod.Today => "Today",
        WatchStatsPeriod.Days7 => "Last 7 days",
        WatchStatsPeriod.Days30 => "Last 30 days",
        WatchStatsPeriod.Year => "Last year",
        WatchStatsPeriod.All => "All time",
        WatchStatsPeriod.Custom when customStart.HasValue && customEnd.HasValue =>
            $"{customStart.Value:MMM d, yyyy} – {customEnd.Value:MMM d, yyyy}",
        WatchStatsPeriod.Custom => "Custom range",
        _ => period.ToString()
    };

    public static string Label(WatchStatsRange range) =>
        Label(range.Period, range.CustomStartUtc, range.CustomEndUtc);

    public static string ShortLabel(WatchStatsPeriod period) => period switch
    {
        WatchStatsPeriod.Today => "Today",
        WatchStatsPeriod.Days7 => "7d",
        WatchStatsPeriod.Days30 => "30d",
        WatchStatsPeriod.Year => "1y",
        WatchStatsPeriod.All => "All",
        WatchStatsPeriod.Custom => "Custom",
        _ => period.ToString()
    };

    public static bool UsesMonthlyBuckets(WatchStatsPeriod period) =>
        period is WatchStatsPeriod.Year or WatchStatsPeriod.All;

    public static bool RangesEqual(WatchStatsRange left, WatchStatsRange right) =>
        left.Period == right.Period
        && left.CustomStartUtc == right.CustomStartUtc
        && left.CustomEndUtc == right.CustomEndUtc;

    public static WatchStatsRange ParseRange(string? period, string? start, string? end)
    {
        var parsedPeriod = Parse(period);
        return new WatchStatsRange(parsedPeriod, TryParseUtcDate(start), TryParseUtcDate(end));
    }

    private static DateTime? TryParseUtcDate(string? value) =>
        DateTime.TryParse(value, out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;
}

public sealed record WatchStatsUserAlias(string CanonicalName, string Source, string SourceUserName);

public sealed record WatchStatsSummary(
    double TotalHours,
    int TotalPlays,
    int UniqueUsers,
    DateTimeOffset? LastPlayAt);

public sealed record WatchStatRow(
    string Title,
    string? Subtitle,
    double Hours,
    int Plays,
    string? ThumbUrl,
    string? ExternalUrl,
    string? Source,
    IReadOnlyList<string>? SourceBreakdown = null,
    string? DrilldownKey = null,
    ActivityDrilldownKind? DrilldownKind = null,
    DateTimeOffset? LastPlayedAtUtc = null,
    /// <summary>Second-chance poster (title/provider-id search) tried client-side if
    /// ThumbUrl 404s or resolves to nothing — e.g. a native Plex/Emby thumb that's gone
    /// stale after the server re-matched the item.</summary>
    string? FallbackThumbUrl = null);

public enum ActivityDrilldownKind
{
    User,
    Media,
    Genre,
    Platform,
    MediaType,
    Quality,
    Library
}

public sealed record ActivityDrilldownRequest(
    ActivityDrilldownKind Kind,
    string Key,
    string Title,
    string? Source,
    WatchStatsPeriod Period,
    DateTime? CustomStartUtc = null,
    DateTime? CustomEndUtc = null,
    // Header text only (e.g. "True Blood — Scratches"); Title stays series-level so
    // ActivityDrilldownProfiler's title-based matching against stored/live events still works.
    string? DisplayLabel = null);

public sealed record ActivityDrilldownPlay(
    string Title,
    string? Subtitle,
    string UserDisplayName,
    double Hours,
    DateTimeOffset PlayedAtUtc,
    string? ThumbUrl,
    string? Source,
    string? MediaType = null,
    IReadOnlyList<string>? Genres = null,
    string? UserExternalId = null,
    string? Platform = null,
    // The item's own title (episode name), distinct from Title/Subtitle which are series-level.
    string? EpisodeLabel = null,
    // Opaque per-client drilldown key (e.g. "r:12345" for Tautulli, "tid:12345" for Tracearr)
    // identifying this exact episode/movie so it can be clicked into.
    string? MediaKey = null);

public sealed record ActivityDrilldownMediaMix(
    double MovieHours,
    double TvHours,
    double MusicHours,
    double OtherHours)
{
    public double TotalHours => MovieHours + TvHours + MusicHours + OtherHours;
}

public sealed record ActivityDrilldownSnapshot(
    ActivityDrilldownRequest Request,
    IReadOnlyList<ActivityDrilldownPlay> Plays,
    double TotalHours,
    int TotalPlays,
    int UniqueUsers = 0,
    int UniqueTitles = 0,
    IReadOnlyList<WatchStatRow>? TopUsers = null,
    IReadOnlyList<WatchStatRow>? TopMovies = null,
    IReadOnlyList<WatchStatRow>? TopTv = null,
    IReadOnlyList<WatchStatRow>? TopGenres = null,
    ActivityDrilldownMediaMix? MediaMix = null);

public sealed record WatchStatsSection(
    string Key,
    string Label,
    IReadOnlyList<WatchStatRow> Rows);

public sealed record WatchStatsSourceSnapshot(
    string Key,
    string Label,
    bool Available,
    string? Error,
    WatchStatsSummary Summary,
    IReadOnlyList<WatchStatRow> TopUsers,
    IReadOnlyList<WatchStatRow> TopGenres,
    IReadOnlyList<WatchStatRow> TopMovies,
    IReadOnlyList<WatchStatRow> TopTv,
    IReadOnlyList<WatchStatRow> TopMusic,
    IReadOnlyList<WatchStatRow> TopPlatforms,
    IReadOnlyList<WatchStatRow> Recent,
    IReadOnlyList<WatchStatRow>? TopPopularMovies = null,
    IReadOnlyList<WatchStatRow>? TopPopularTv = null,
    IReadOnlyList<WatchStatRow>? TopLibraries = null);

public sealed record WatchStatsSnapshot(
    WatchStatsPeriod Period,
    WatchStatsSourceFilter Filter,
    WatchStatsSourceSnapshot? Combined,
    IReadOnlyList<WatchStatsSourceSnapshot> Sources,
    DateTimeOffset UpdatedAt,
    WatchStatsSyncStatus? SyncStatus);

public enum WatchStatsSourceSyncState
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

public sealed record WatchStatsSourceSyncProgress(
    string Source,
    string Label,
    WatchStatsSourceSyncState State,
    string Message,
    int ImportedCount,
    int FetchedCount,
    DateTimeOffset? LastSyncedAt,
    string? Error);

public sealed record WatchStatsSyncStatus(
    bool IsRunning,
    string? CurrentMessage,
    int ProgressPercent,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastSyncAt,
    long TotalEvents,
    IReadOnlyList<WatchStatsSourceSyncProgress> Sources,
    IReadOnlyDictionary<string, string?> SourceErrors);

public static class WatchStatsSources
{
    public const string Plex = "plex";
    public const string Emby = "emby";
    public const string Jellyfin = "jellyfin";
    public const string Trakt = "trakt";

    public static string Label(string source) => source switch
    {
        Plex => "Plex",
        Emby => "Emby",
        Jellyfin => "Jellyfin",
        Trakt => "Trakt",
        _ => source
    };
}

public sealed record WatchStatsLibraryInfo(
    string Source,
    string ExternalId,
    string Name,
    string? MediaType = null)
{
    public string Key => $"{Source}:{ExternalId}";
}
