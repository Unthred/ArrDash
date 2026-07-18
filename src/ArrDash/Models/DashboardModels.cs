namespace ArrDash.Models;

public enum MediaSource
{
    Sonarr,
    Radarr,
    Lidarr,
    Chaptarr,
    AudiobookShelf,
    Slskd
}

public enum MediaType
{
    Tv,
    Movie,
    Audiobook,
    Music,
    Unknown
}

public enum DownloadEvent
{
    Grabbed,
    Imported,
    Completed,
    Failed,
    Other
}

public enum StreamingServer
{
    Plex,
    Emby,
    Jellyfin
}

public enum ThemePreference
{
    Light,
    Dark,
    System
}

public enum PanelViewMode
{
    Cards,
    Compact,
    Table
}

public sealed record DownloadItem(
    string Id,
    MediaSource Source,
    MediaType MediaType,
    string Title,
    string? Subtitle,
    DownloadEvent Event,
    DateTimeOffset Timestamp,
    string? PosterUrl,
    string? Quality,
    long? SizeBytes,
    int? SeasonNumber = null,
    IReadOnlyList<int>? EpisodeNumbers = null,
    string? StatusNote = null,
    string? ExternalUrl = null,
    int? SeriesId = null,
    IReadOnlyList<int>? BadgeEpisodeNumbers = null,
    IReadOnlyList<int>? OnDiskEpisodeNumbers = null,
    IReadOnlyList<int>? UnairedEpisodeNumbers = null,
    IReadOnlyDictionary<int, DateTimeOffset>? EpisodeAirDates = null,
    string? Asin = null,
    string? ImdbId = null,
    int? TmdbId = null,
    int? TvdbId = null,
    string? YouTubeTrailerId = null,
    string? ChaptarrUrl = null,
    string? AudiobookShelfUrl = null,
    string? GoodreadsUrl = null,
    string? HardcoverUrl = null);

public sealed record MediaQuickLink(string Label, string Url, string CssClass);

public sealed record ActiveSession(
    string Id,
    StreamingServer Server,
    string User,
    string Title,
    string? Subtitle,
    string MediaType,
    double ProgressPercent,
    string? Device,
    string? ThumbnailUrl,
    DateTimeOffset? StartedAt,
    string? ExternalUrl = null,
    bool? IsLocal = null,
    int? BitrateKbps = null,
    int? BandwidthKbps = null,
    string? Resolution = null);

public enum ServiceWorkloadKind
{
    None,
    Searching,
    Importing,
    Downloading,
    Scanning,
    Syncing,
    Transcoding,
    Matching
}

public sealed record ServiceWorkload(
    ServiceWorkloadKind Kind,
    string Label,
    int? Count = null);

public sealed record ServiceHealth(
    string Key,
    string Name,
    bool Configured,
    bool Online,
    string? Error,
    string? Version,
    ServiceWorkload? Workload = null,
    ServiceAttentionLevel Attention = ServiceAttentionLevel.Healthy,
    string? AttentionLabel = null,
    int? AttentionCount = null);

public enum ServiceAttentionLevel
{
    NotConfigured,
    Offline,
    Healthy,
    Busy,
    NeedsAttention
}

public sealed record ServiceProblem(string Severity, string Message, string? Url = null);

public sealed record ServiceQueueStateCount(string State, int Count);

public sealed record ServiceCommandInfo(string Name, string Status, DateTimeOffset? StartedAt);

public sealed record ServiceRecentActivity(string Title, string EventLabel, DateTimeOffset At);

public sealed record ServiceSessionSummary(string Title, string? Detail, int? BandwidthKbps, bool? IsLocal);

public sealed record ServiceDetail(
    string Key,
    string Name,
    bool Configured,
    bool Online,
    string? Version,
    string? ServiceUrl,
    DateTimeOffset SampledAt,
    ServiceAttentionLevel Attention,
    string AttentionLabel,
    ServiceWorkload? Workload,
    IReadOnlyList<ServiceProblem> Problems,
    IReadOnlyList<ServiceQueueStateCount> QueueStates,
    int QueueTotal,
    bool QueueHasErrors,
    bool QueueHasWarnings,
    IReadOnlyList<ServiceCommandInfo> Commands,
    IReadOnlyList<ServiceRecentActivity> RecentActivity,
    IReadOnlyList<ServiceSessionSummary> Sessions,
    string? ConnectionError = null);

public sealed record ServerMetrics(
    string Label,
    double? CpuPercent,
    double? IoWaitPercent,
    double MemoryUsedPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double DiskUsedPercent,
    long DiskUsedBytes,
    long DiskTotalBytes);

public sealed record DiskInfo(
    string Name,
    string? Type,
    double? TempCelsius,
    bool SpunDown,
    string? FilesystemType,
    long? SizeBytes,
    long? UsedBytes,
    long? FreeBytes,
    bool Rotational,
    string? Status,
    int NumErrors,
    double? BusyPercent);

public sealed record DiskHealthSummary(
    bool AllHealthy,
    IReadOnlyList<string> UnhealthyDisks,
    double? HottestTempCelsius,
    string? HottestDiskName,
    IReadOnlyList<DiskInfo> AllDisks);

public sealed record ContainerUsage(string Name, double CpuPercent, double CpuCores, double MemoryUsedPercent);

public sealed record CpuCoreTemp(string Label, double TempCelsius);

public sealed record CoreUsage(int Index, double Percent);

public sealed record CpuSample(DateTimeOffset At, double Percent, double IoWaitPercent);

public sealed record NetworkThroughput(long RxBytesPerSecond, long TxBytesPerSecond);

public enum StuckProcessKind
{
    DockerContainer,
    SystemProcess,
    KernelWorker
}

public sealed record StuckProcess(
    int Pid,
    string DisplayName,
    StuckProcessKind Kind,
    string ProcessName,
    string? ContainerName,
    string? CommandLine);

public sealed record UnraidActivity(
    bool ParityCheckRunning,
    double? ParityCheckPercent,
    string ParityCheckLabel,
    bool MoverRunning,
    DateTimeOffset? MoverStartedAt,
    IReadOnlyList<string> RestartingContainers,
    IReadOnlyList<string> RecentlyUpdatedContainers,
    DiskHealthSummary? DiskHealth,
    IReadOnlyList<ContainerUsage> TopContainers,
    TimeSpan? Uptime,
    double? CpuTemperatureCelsius,
    IReadOnlyList<CpuCoreTemp> CpuCoreTemps,
    NetworkThroughput? Network,
    bool IsUnraidHost,
    IReadOnlyList<StuckProcess> StuckProcesses);

public sealed record LibraryStatItem(
    string Key,
    string Label,
    string Headline,
    string? Detail,
    string AccentColor,
    string? Url,
    long? ItemCount = null);

public sealed record DashboardSnapshot(
    IReadOnlyList<DownloadItem> RecentTv,
    IReadOnlyList<DownloadItem> RecentMovies,
    IReadOnlyList<DownloadItem> RecentAudiobooks,
    IReadOnlyList<DownloadItem> RecentMusic,
    IReadOnlyList<ActiveSession> ActiveSessions,
    IReadOnlyList<ServiceHealth> Services,
    DateTimeOffset UpdatedAt,
    ServerMetrics? Host = null);

public sealed class PanelDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public string? AccentColor { get; init; }
    public bool Collapsed { get; set; }
    public PanelViewMode ViewMode { get; set; } = PanelViewMode.Cards;
}

public sealed class UserLayoutPreferences
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool KioskMode { get; set; }
    public string DashboardTitle { get; set; } = "Your media universe";
    public string DashboardSubtitle { get; set; } = "Recent downloads and live playback across Sonarr, Radarr, Chaptarr, Plex, and Emby.";
    public PosterSize PosterSize { get; set; } = PosterSize.Medium;
    public PosterPlacement PosterPlacement { get; set; } = PosterPlacement.Left;
    public int DefaultRecentLimit { get; set; } = 20;
    public List<string> PanelOrder { get; set; } =
    [
        "now-playing",
        "watch-stats",
        "libraries",
        "recent-tv",
        "recent-movies",
        "recent-audiobooks",
        "recent-music"
    ];
    public Dictionary<string, PanelViewMode> PanelViewModes { get; set; } = new();
    public Dictionary<string, bool> PanelCollapsed { get; set; } = new();
    public Dictionary<string, bool> PanelRolledUp { get; set; } = new();
    public Dictionary<string, string> PanelAccentColors { get; set; } = new()
    {
        ["now-playing"] = "#818cf8",
        ["watch-stats"] = "#a855f7",
        ["libraries"] = "#22c55e",
        ["recent-tv"] = "#35c5f4",
        ["recent-movies"] = "#f5c518",
        ["recent-audiobooks"] = "#00d2be",
        ["recent-music"] = "#bc93e1"
    };
    public Dictionary<string, int> RecentLimits { get; set; } = new();
    public LayoutDensity Density { get; set; } = LayoutDensity.Comfortable;
    public bool HideHeroStrip { get; set; }
    public StatusBarMode StatusBarMode { get; set; } = StatusBarMode.All;
    public StatusBarAlignment StatusBarAlignment { get; set; } = StatusBarAlignment.Left;
    public TimeDisplayFormat TimeFormat { get; set; } = TimeDisplayFormat.Relative;
    public RecentWindowMode RecentWindowMode { get; set; } = RecentWindowMode.ItemCount;
    public int RecentDays { get; set; } = 30;
    public AudiobookSourceMode AudiobookSource { get; set; } = AudiobookSourceMode.Merge;
    public bool ShowPlexSessions { get; set; } = true;
    public bool ShowEmbySessions { get; set; } = true;
    public bool ShowJellyfinSessions { get; set; } = true;
    public bool HideIdleSessions { get; set; }
    public bool ShowPlexWatchStats { get; set; } = true;
    public bool ShowEmbyWatchStats { get; set; } = true;
    public bool ShowJellyfinWatchStats { get; set; } = true;
    public bool ShowCombinedWatchStats { get; set; } = true;
    public bool WatchStatsSyncEnabled { get; set; } = true;
    public bool WatchStatsSyncPlex { get; set; } = true;
    public bool WatchStatsSyncEmby { get; set; } = true;
    public bool WatchStatsSyncJellyfin { get; set; } = true;
    public int WatchStatsRetentionDays { get; set; }
    public int WatchStatsBackfillDays { get; set; }
    public int WatchStatsSyncIntervalMinutes { get; set; }
    public int WatchStatsTopLimit { get; set; } = 10;
    /// <summary>Legacy include-list; superseded by <see cref="WatchStatsExcludedLibraries"/> (#38).</summary>
    public List<string> WatchStatsIncludedLibraries { get; set; } = [];
    /// <summary>Composite keys <c>source:libraryExternalId</c> hidden from Activity and never pushed to Trakt.
    /// Events with an unknown library stay visible; unknown-library events are excluded from Trakt push.</summary>
    public List<string> WatchStatsExcludedLibraries { get; set; } = [];
    /// <summary>Poster source for Trakt history items: off | tmdb | library | both (library first, TMDB fallback).</summary>
    public string TraktPosterMode { get; set; } = "both";
    public List<WatchStatsUserAlias> UserAliases { get; set; } = [];
    public List<ActivityLayoutItem> ActivityLayout { get; set; } = [];
    public bool ShowServerMetrics { get; set; } = true;
    public string MetricsHostLabel { get; set; } = "";
    public string MetricsDiskPath { get; set; } = "";
    public bool EnableClickThrough { get; set; } = true;
    public bool DeepLinkClickThrough { get; set; } = true;
    public ExternalLinkTarget ExternalLinkTarget { get; set; } = ExternalLinkTarget.NewTab;
    public MissingEpisodeClickAction MissingEpisodeClickAction { get; set; } = MissingEpisodeClickAction.SearchOnly;
    public bool FriendlyQualityLabels { get; set; } = true;
    public string PrimaryColor { get; set; } = "#6366f1";
    public string SecondaryColor { get; set; } = "#0ea5e9";
    public string ButtonColor { get; set; } = "";
    public string LightTextColor { get; set; } = "#0f172a";
    public string DarkTextColor { get; set; } = "#eef2ff";
    public string LightBackgroundColor { get; set; } = "#f4f6fb";
    public string DarkBackgroundColor { get; set; } = "#0b1020";
    public string LightSurfaceColor { get; set; } = "#ffffff";
    public string DarkSurfaceColor { get; set; } = "#121829";
    public string LightAppBarColor { get; set; } = "";
    public string DarkAppBarColor { get; set; } = "";
    public string GradientStartColor { get; set; } = "#6366f1";
    public string GradientEndColor { get; set; } = "#0ea5e9";
    public BackgroundStyle BackgroundStyle { get; set; } = BackgroundStyle.Gradient;
    public string BrandMark { get; set; } = "A";
    public BorderRadiusStyle BorderRadius { get; set; } = BorderRadiusStyle.Rounded;
    public bool ShowQuality { get; set; } = true;
    public bool ShowEpisodeBadges { get; set; } = true;
    public bool ShowMissingEpisodes { get; set; } = true;
    public bool ShowSyncNotes { get; set; } = true;
    public int PollIntervalSeconds { get; set; }
    public int MetricsPollIntervalSeconds { get; set; }
    public int MetricsGraphWindowMinutes { get; set; }
    public int NetworkPieChartSize { get; set; } = 160;
    public NetworkPieStyle NetworkPieStyle { get; set; } = NetworkPieStyle.Soft;
    public bool NetworkPieShowSliceLabels { get; set; } = true;
    public bool NetworkLegendShowPercent { get; set; }
    public bool NetworkLegendShowRate { get; set; }
    public bool NetworkLegendShowCpu { get; set; }
    public bool ManualRefreshOnly { get; set; }
    public StartupPage StartupPage { get; set; } = StartupPage.Dashboard;
    public Dictionary<string, bool> ServiceEnabled { get; set; } = new();
    public bool AutoKioskOnLoad { get; set; }
    public bool KioskHideHero { get; set; } = true;
    public bool KioskLargeNowPlaying { get; set; }
    public bool KioskScreensaver { get; set; }
    public int KioskScreensaverMinutes { get; set; } = 5;
    public KioskRotateMode KioskRotate { get; set; } = KioskRotateMode.Off;
    public int KioskRotateSeconds { get; set; } = 30;
    public bool ShowSettingsHelp { get; set; }
}

public enum NetworkBandwidthDirection
{
    Download,
    Upload
}

public sealed record NetworkBandwidthDetailItem(string Label, string? Subtitle);

public sealed record NetworkBandwidthRow(
    string Key,
    string Label,
    long BytesPerSecond,
    double PercentOfAttributed,
    IReadOnlyList<NetworkBandwidthDetailItem> DetailItems,
    string? ServiceUrl = null,
    string Source = "container",
    double? CpuPercent = null);

public sealed record NetworkBandwidthDetail(
    NetworkBandwidthDirection Direction,
    long TotalBytesPerSecond,
    long AttributedBytesPerSecond,
    long UnattributedBytesPerSecond,
    DateTimeOffset SampledAt,
    IReadOnlyList<NetworkBandwidthRow> Rows,
    string? Note);
