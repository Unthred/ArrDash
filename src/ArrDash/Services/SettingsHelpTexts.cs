namespace ArrDash.Services;

public static class SettingsHelpTexts
{
    public const string DashboardTitle = "Main heading at the top of the dashboard.";
    public const string HideHeroStrip = "Hides the large title and subtitle block so panels start higher on the page.";
    public const string DashboardSubtitle = "Short line under the dashboard title in the hero strip.";
    public const string PosterSize = "How large artwork thumbnails appear on recent-item cards.";
    public const string PosterPlacement = "Whether poster art sits to the left of item text or above it.";
    public const string ShowQuality = "Shows quality labels (e.g. WEB-DL, FLAC) on recent downloads where available.";
    public const string ShowEpisodeBadges = "Shows the episode number row on TV cards (Cards view). Turn off to hide season episode badges entirely.";
    public const string ShowMissingEpisodes = "When episode badges are on, missing episodes get a dashed highlight. Click a dashed badge to trigger a Sonarr search.";
    public const string ShowSyncNotes = "Shows Chaptarr ↔ AudioBookShelf sync notes on merged audiobook cards.";
    public const string PanelOrder = "Reorder panels with the arrows. Hide removes a panel from the dashboard. View switches between cards, compact list, or table.";

    public const string Theme = "Light, dark, or follow your browser / system preference.";
    public const string Density = "Comfortable adds more spacing; compact fits more on screen.";
    public const string BorderRadius = "Rounded or sharp corners on cards and panels.";
    public const string BrandMark = "One to three characters shown in the app bar brand badge.";
    public const string BackgroundStyle = "Gradient adds soft colour washes; solid is flat; minimal is nearly plain.";
    public const string AccentColor = "Buttons, links, progress bars, and other accent highlights.";
    public const string TextColor = "Main body and heading text for the active theme mode.";
    public const string LightTextColor = "Body and heading text when the dashboard is in light mode — usually a dark colour.";
    public const string DarkTextColor = "Body and heading text when the dashboard is in dark mode — usually a light colour.";

    public static string TextColorSystem(bool isDark) =>
        $"System theme is currently {(isDark ? "dark" : "light")}. Set text colour for each mode below.";

    public const string LightPage = "Background behind the whole page in light mode.";
    public const string LightBackgroundColor = LightPage;
    public const string LightSurface = "Card and panel background in light mode.";
    public const string LightAppBar = "Top navigation bar in light mode. Default matches the surface colour.";
    public const string DarkPage = "Background behind the whole page in dark mode.";
    public const string DarkBackgroundColor = DarkPage;
    public const string DarkSurface = "Card and panel background in dark mode.";
    public const string DarkAppBar = "Top navigation bar in dark mode. Default matches the surface colour.";
    public const string GradientStart = "Starting colour for the decorative background gradient.";
    public const string GradientEnd = "Ending colour for the decorative background gradient.";

    public static string SystemBackground(bool isDark) =>
        $"Theme follows your system — currently {(isDark ? "dark" : "light")}, so only those background colours are shown here. Choose Light or Dark theme to edit the other set.";

    public const string RecentWindow = "Limit recent lists by number of items or by how many days back to look.";
    public const string TimeFormat = "How timestamps appear — relative (\"2h ago\"), clock only, or full date and time.";
    public const string RecentDays = "When using days mode, only items from this many past days are shown.";
    public const string DefaultRecentLimit = "Default maximum items per recent panel when using item count mode.";
    public const string AudiobookSource = "Which services feed the audiobook panel — both merged, or one only.";

    public const string ShowPlexSessions = "Shows who is watching what on Plex in the Now Playing panel.";
    public const string ShowEmbySessions = "Shows who is watching what on Emby in the Now Playing panel.";
    public const string HideIdleSessions = "Hides sessions that are paused or at 0% progress.";
    public const string ShowServerMetrics = "Shows CPU graph, memory, disk usage, and library counts in the metrics bar.";
    public const string MetricsPollInterval = "How often server CPU and usage rings refresh. 0 uses the default (2 seconds).";
    public const string MetricsGraphWindow = "How many minutes of CPU history the graph displays. 0 uses the default (15 minutes).";
    public const string EnableClickThrough = "Lets you click recent items and library stats to open the source app.";
    public const string DeepLinkClickThrough = "Opens the specific movie, show, book, or artist in the app instead of the app home page.";
    public const string ExternalLinkTarget = "Whether click-through opens the source app in a new browser tab or the current tab.";
    public const string MissingEpisodeClickAction = "When you click a missing TV episode badge: search in Sonarr only, or open Sonarr and start a search.";
    public const string FriendlyQualityLabels = "Shows readable quality labels such as \"2160p · Web-DL\" instead of \"WEBDL-2160p\".";
    public const string PollInterval = "How often the dashboard refreshes data from *arr and media servers. 0 uses the server default.";
    public const string ManualRefreshOnly = "Stops automatic polling — refresh only when you use the refresh control.";
    public const string StatusBar = "Service health chips at the top — show all, offline only, or hide entirely.";
    public const string StartupPage = "Which page opens when you visit ArrDash.";

    public const string AutoKioskOnLoad = "Enters full-screen kiosk mode automatically when the dashboard loads.";
    public const string KioskHideHero = "Hides the title strip while in kiosk mode for a cleaner TV layout.";
    public const string KioskLargeNowPlaying = "Makes the Now Playing panel larger in kiosk mode.";
    public const string KioskScreensaver = "Dims the screen after idle time when in kiosk mode.";
    public const string KioskScreensaverMinutes = "Minutes of no interaction before the screensaver activates.";
    public const string KioskRotateSeconds = "Seconds each panel stays visible when rotating in kiosk mode.";
    public const string KioskRotate = "In kiosk mode: show all panels, rotate through them, or Now Playing only.";

    public const string ServicesEnabled = "Turn off a service to stop ArrDash fetching from it — useful if an app is down or unused.";
    public const string ApiServicePicker = "Choose which service to configure credentials for.";
    public const string ApiUrl = "Base URL for the service API. Use an HTTPS hostname reachable from the ArrDash container.";
    public const string ApiKey = "Paste a new key to replace the saved one. Leave blank to keep the existing key.";

    public static string PanelRecentLimit(string panelLabel) =>
        $"Maximum items in the {panelLabel} panel. Ignored when recent window is set to days.";

    public static string ServiceEnabled(string label) =>
        $"Include {label} when loading the dashboard. Disabled services are skipped entirely.";

    public static string PanelAccent(string panelLabel) =>
        $"Highlight colour for the {panelLabel} panel.";
}
