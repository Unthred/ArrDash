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
    JellyfinClient jellyfin,
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
        var jellyfinTask = FetchSessionsIfEnabled("jellyfin", jellyfin.FetchSessionsAsync(ct));

        await Task.WhenAll(sonarrTask, radarrTask, lidarrTask, chaptarrTask, audiobookShelfTask, plexTask, embyTask, jellyfinTask);

        var (tvRaw, sonarrHealth) = await sonarrTask;
        var (moviesRaw, radarrHealth) = await radarrTask;
        var (musicRaw, lidarrHealth) = await lidarrTask;
        var (chaptarrDownloads, chaptarrHealth) = await chaptarrTask;
        var (libraryAudiobooks, audiobookShelfHealth) = await audiobookShelfTask;
        var (plexSessions, plexHealth) = await plexTask;
        var (embySessions, embyHealth) = await embyTask;
        var (jellyfinSessions, jellyfinHealth) = await jellyfinTask;

        var p = prefs.Current;
        var tv = RecentItemFilter.Apply(tvRaw, p, "recent-tv");
        var movies = RecentItemFilter.Apply(moviesRaw, p, "recent-movies");
        var music = RecentItemFilter.Apply(musicRaw, p, "recent-music");
        var audiobooks = ApplyAudiobookSource(chaptarrDownloads, libraryAudiobooks, p);

        var sessions = FilterSessions(plexSessions, embySessions, jellyfinSessions, p);

        var services = new List<ServiceHealth>
        {
            DisabledIfNeeded(sonarrHealth, "sonarr"),
            DisabledIfNeeded(radarrHealth, "radarr"),
            DisabledIfNeeded(chaptarrHealth, "chaptarr"),
            DisabledIfNeeded(audiobookShelfHealth, "audiobookshelf"),
            DisabledIfNeeded(lidarrHealth, "lidarr"),
            DisabledIfNeeded(plexHealth, "plex"),
            DisabledIfNeeded(embyHealth, "emby"),
            DisabledIfNeeded(jellyfinHealth, "jellyfin"),
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
        IReadOnlyList<ActiveSession> jellyfin,
        UserLayoutPreferences p)
    {
        IEnumerable<ActiveSession> sessions = [];
        if (p.ShowPlexSessions)
            sessions = sessions.Concat(plex);
        if (p.ShowEmbySessions)
            sessions = sessions.Concat(emby);
        if (p.ShowJellyfinSessions)
            sessions = sessions.Concat(jellyfin);

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

public interface IDashboardRefresher
{
    Task RefreshAsync(CancellationToken ct);
}

public sealed class DashboardRefreshService(
    DashboardCollector collector,
    DashboardState state,
    IHubContext<DashboardHub> hub,
    IHostApplicationLifetime lifetime) : IDashboardRefresher
{
    private readonly object _refreshLock = new();
    private Task? _inFlight;

    public async Task RefreshAsync(CancellationToken ct)
    {
        Task refresh;
        lock (_refreshLock)
            refresh = _inFlight ??= RefreshCoreAsync();

        // A browser disconnect or a stale-check timer cancellation must only stop that caller
        // waiting. It must not cancel the shared collection for every connected dashboard.
        await refresh.WaitAsync(ct);
    }

    private async Task RefreshCoreAsync()
    {
        state.SetRefreshing(true);
        try
        {
            var snapshot = await collector.CollectAsync(lifetime.ApplicationStopping);
            state.Update(snapshot);
            await hub.Clients.All.SendAsync("DashboardUpdated", snapshot, lifetime.ApplicationStopping);
        }
        finally
        {
            state.SetRefreshing(false);
            lock (_refreshLock)
                _inFlight = null;
        }
    }
}
