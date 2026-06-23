using ArrDash.Hubs;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.AspNetCore.SignalR;

namespace ArrDash.Services;

public sealed class DashboardCollector(
    SonarrClient sonarr,
    RadarrClient radarr,
    LidarrClient lidarr,
    ChaptarrClient chaptarr,
    AudiobookShelfClient audiobookShelf,
    PlexClient plex,
    EmbyClient emby,
    LayoutPreferencesService prefs,
    MediaServiceOptionsAccessor options,
    HostSystemMetricsService hostMetrics)
{
    public async Task<DashboardSnapshot> CollectAsync(CancellationToken ct)
    {
        var fetchLimit = Math.Clamp(prefs.Current.DefaultRecentLimit * 3, 20, 150);

        var sonarrTask = FetchIfEnabled("sonarr", sonarr.FetchRecentAsync(fetchLimit, ct));
        var radarrTask = FetchIfEnabled("radarr", radarr.FetchRecentAsync(fetchLimit, ct));
        var lidarrTask = FetchIfEnabled("lidarr", lidarr.FetchRecentAsync(fetchLimit, ct));
        var chaptarrTask = FetchIfEnabled("chaptarr", chaptarr.FetchRecentAsync(fetchLimit, ct));
        var audiobookShelfTask = FetchIfEnabled("audiobookshelf", audiobookShelf.FetchRecentAsync(fetchLimit, ct));
        var plexTask = FetchSessionsIfEnabled("plex", plex.FetchSessionsAsync(ct));
        var embyTask = FetchSessionsIfEnabled("emby", emby.FetchSessionsAsync(ct));

        await Task.WhenAll(sonarrTask, radarrTask, lidarrTask, chaptarrTask, audiobookShelfTask, plexTask, embyTask);

        var (tvRaw, sonarrHealth) = await sonarrTask;
        var (moviesRaw, radarrHealth) = await radarrTask;
        var (musicRaw, lidarrHealth) = await lidarrTask;
        var (chaptarrDownloads, chaptarrHealth) = await chaptarrTask;
        var (libraryAudiobooks, audiobookShelfHealth) = await audiobookShelfTask;
        var (plexSessions, plexHealth) = await plexTask;
        var (embySessions, embyHealth) = await embyTask;

        var p = prefs.Current;
        var tv = RecentItemFilter.Apply(tvRaw, p, "recent-tv");
        var movies = RecentItemFilter.Apply(moviesRaw, p, "recent-movies");
        var music = RecentItemFilter.Apply(musicRaw, p, "recent-music");
        var audiobooks = ApplyAudiobookSource(chaptarrDownloads, libraryAudiobooks, p);

        var sessions = FilterSessions(plexSessions, embySessions, p);

        var services = new List<ServiceHealth>
        {
            DisabledIfNeeded(sonarrHealth, "sonarr"),
            DisabledIfNeeded(radarrHealth, "radarr"),
            DisabledIfNeeded(chaptarrHealth, "chaptarr"),
            DisabledIfNeeded(audiobookShelfHealth, "audiobookshelf"),
            DisabledIfNeeded(lidarrHealth, "lidarr"),
            DisabledIfNeeded(plexHealth, "plex"),
            DisabledIfNeeded(embyHealth, "emby"),
            new("slskd", "slskd", options.Options.Slskd.IsConfigured, false,
                options.Options.Slskd.IsConfigured ? null : "API key not configured", null)
        };

        return new DashboardSnapshot(
            tv,
            movies,
            audiobooks,
            music,
            sessions,
            services,
            DateTimeOffset.UtcNow,
            prefs.Current.ShowServerMetrics ? hostMetrics.Read() : null);
    }

    private static IReadOnlyList<DownloadItem> ApplyAudiobookSource(
        IReadOnlyList<DownloadItem> chaptarr,
        IReadOnlyList<DownloadItem> library,
        UserLayoutPreferences p)
    {
        var limit = RecentItemFilter.ResolveLimit(p, "recent-audiobooks");

        IEnumerable<DownloadItem> merged = p.AudiobookSource switch
        {
            AudiobookSourceMode.ChaptarrOnly => chaptarr,
            AudiobookSourceMode.AudiobookShelfOnly => library,
            _ => AudiobookMergeService.Merge(chaptarr, library, limit)
        };

        if (p.RecentWindowMode == RecentWindowMode.Days)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(p.RecentDays, 1, 365));
            merged = merged.Where(i => i.Timestamp >= cutoff);
        }

        return merged
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<ActiveSession> FilterSessions(
        IReadOnlyList<ActiveSession> plex,
        IReadOnlyList<ActiveSession> emby,
        UserLayoutPreferences p)
    {
        IEnumerable<ActiveSession> sessions = [];
        if (p.ShowPlexSessions)
            sessions = sessions.Concat(plex);
        if (p.ShowEmbySessions)
            sessions = sessions.Concat(emby);

        if (p.HideIdleSessions)
            sessions = sessions.Where(s => s.ProgressPercent > 0.5);

        return sessions
            .OrderByDescending(s => s.ProgressPercent)
            .ToList();
    }

    private Task<(IReadOnlyList<DownloadItem>, ServiceHealth)> FetchIfEnabled(
        string key,
        Task<(IReadOnlyList<DownloadItem>, ServiceHealth)> task) =>
        prefs.IsServiceEnabled(key)
            ? task
            : Task.FromResult<(IReadOnlyList<DownloadItem>, ServiceHealth)>(
                ([], new ServiceHealth(key, key, false, false, "Disabled in settings", null)));

    private Task<(IReadOnlyList<ActiveSession>, ServiceHealth)> FetchSessionsIfEnabled(
        string key,
        Task<(IReadOnlyList<ActiveSession>, ServiceHealth)> task) =>
        prefs.IsServiceEnabled(key)
            ? task
            : Task.FromResult<(IReadOnlyList<ActiveSession>, ServiceHealth)>(
                ([], new ServiceHealth(key, key, false, false, "Disabled in settings", null)));

    private ServiceHealth DisabledIfNeeded(ServiceHealth health, string key) =>
        prefs.IsServiceEnabled(key)
            ? health
            : health with { Configured = false, Online = false, Error = "Disabled in settings" };
}

public sealed class DashboardRefreshService(
    DashboardCollector collector,
    DashboardState state,
    IHubContext<DashboardHub> hub)
{
    public async Task RefreshAsync(CancellationToken ct)
    {
        var snapshot = await collector.CollectAsync(ct);
        state.Update(snapshot);
        await hub.Clients.All.SendAsync("DashboardUpdated", snapshot, ct);
    }
}
