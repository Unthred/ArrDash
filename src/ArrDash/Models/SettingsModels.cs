namespace ArrDash.Models;

public enum PosterSize
{
    Small,
    Medium,
    Large
}

public enum PosterPlacement
{
    Left,
    Top
}

public enum LayoutDensity
{
    Comfortable,
    Compact
}

public enum TimeDisplayFormat
{
    Relative,
    Clock,
    DateTime
}

public enum RecentWindowMode
{
    ItemCount,
    Days
}

public enum AudiobookSourceMode
{
    Merge,
    ChaptarrOnly,
    AudiobookShelfOnly
}

public enum BackgroundStyle
{
    Gradient,
    Solid,
    Minimal
}

public enum NetworkPieStyle
{
    Flat,
    Soft,
    Glossy
}

public enum BorderRadiusStyle
{
    Rounded,
    Sharp
}

public enum MissingEpisodeClickAction
{
    SearchOnly,
    OpenSonarrAndSearch
}

public enum ExternalLinkTarget
{
    NewTab,
    SameTab
}

public enum StartupPage
{
    Dashboard,
    Settings
}

public enum StatusBarMode
{
    All,
    OfflineOnly,
    Hidden
}

public enum StatusBarAlignment
{
    Left,
    Right,
    SpacedEvenly
}

public enum KioskRotateMode
{
    Off,
    Sequential,
    NowPlayingOnly
}

public sealed record DashboardDisplaySettings(
    string Title,
    string Subtitle,
    PosterSize PosterSize,
    PosterPlacement PosterPlacement,
    IReadOnlyDictionary<string, string> PanelAccents);

public sealed record CredentialStatus(
    string Key,
    string Label,
    bool Configured,
    string? MaskedHint,
    string Instructions,
    string HelpUrl,
    string? CurrentUrl);

public sealed class ServiceCredentialInput
{
    public string SonarrApiKey { get; set; } = "";
    public string RadarrApiKey { get; set; } = "";
    public string LidarrApiKey { get; set; } = "";
    public string ChaptarrApiKey { get; set; } = "";
    public string AudiobookShelfApiKey { get; set; } = "";
    public string SlskdApiKey { get; set; } = "";
    public string PlexToken { get; set; } = "";
    public string EmbyApiKey { get; set; } = "";
    public string JellyfinApiKey { get; set; } = "";
}

public sealed class ServiceUrlInput
{
    public string SonarrUrl { get; set; } = "";
    public string RadarrUrl { get; set; } = "";
    public string LidarrUrl { get; set; } = "";
    public string ChaptarrUrl { get; set; } = "";
    public string AudiobookShelfUrl { get; set; } = "";
    public string SlskdUrl { get; set; } = "";
    public string PlexUrl { get; set; } = "";
    public string EmbyUrl { get; set; } = "";
    public string JellyfinUrl { get; set; } = "";
}

public static class PanelCatalog
{
    public static readonly (string Id, string Label, string DefaultAccent)[] All =
    [
        ("now-playing", "Now Playing", "#818cf8"),
        ("watch-stats", "Watch Stats", "#a855f7"),
        ("libraries", "Libraries", "#22c55e"),
        ("recent-tv", "Recent TV", "#35c5f4"),
        ("recent-movies", "Recent Movies", "#f5c518"),
        ("recent-audiobooks", "Recent Audiobooks", "#00d2be"),
        ("recent-music", "Recent Music", "#bc93e1"),
        ("chaptarr-abs-sync", "Chaptarr Sync", "#f97316")
    ];

    public static readonly (string Id, string Label)[] Recent =
    [
        ("recent-tv", "Recent TV"),
        ("recent-movies", "Recent Movies"),
        ("recent-audiobooks", "Recent Audiobooks"),
        ("recent-music", "Recent Music")
    ];

    public static readonly string[] DefaultOrder = All.Select(p => p.Id).ToArray();

    public static string LabelFor(string panelId) =>
        All.FirstOrDefault(p => p.Id == panelId).Label ?? panelId;
}
