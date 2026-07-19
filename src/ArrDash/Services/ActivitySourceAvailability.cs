using ArrDash.Models;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public sealed class ActivitySourceAvailability(
    TautulliClient tautulli,
    TracearrClient tracearr,
    EmbyPlaybackReportingClient embyReporting,
    JellyfinPlaybackReportingClient jellyfinReporting,
    TraktAccountService traktAccounts,
    PlexHistoryClient plexHistory)
{
    private IReadOnlyList<TracearrServerInfo>? _servers;
    private DateTimeOffset _serversCachedAt;

    public async Task<IReadOnlyList<WatchStatsSourceFilter>> GetAvailableFiltersAsync(CancellationToken ct = default)
    {
        var filters = new List<WatchStatsSourceFilter>();
        var configured = await GetConfiguredSourcesAsync(ct);

        if (configured.Contains(WatchStatsSources.Plex))
            filters.Add(WatchStatsSourceFilter.Plex);
        if (configured.Contains(WatchStatsSources.Emby))
            filters.Add(WatchStatsSourceFilter.Emby);
        if (configured.Contains(WatchStatsSources.Jellyfin))
            filters.Add(WatchStatsSourceFilter.Jellyfin);
        if (configured.Contains(WatchStatsSources.Trakt))
            filters.Add(WatchStatsSourceFilter.Trakt);

        return filters;
    }

    public async Task<WatchStatsSourceFilter> GetAvailableMaskAsync(CancellationToken ct = default) =>
        WatchStatsSourceFilters.FromConfigured(await GetConfiguredSourcesAsync(ct));


    public async Task<IReadOnlyList<string>> GetConfiguredSourcesAsync(CancellationToken ct = default)
    {
        var list = new List<string>();
        if (tautulli.IsConfigured || plexHistory.IsConfigured)
            list.Add(WatchStatsSources.Plex);

        if (tracearr.IsConfigured)
        {
            foreach (var server in await GetServersAsync(ct))
            {
                var key = server.Type.ToLowerInvariant() switch
                {
                    "plex" => WatchStatsSources.Plex,
                    "emby" => WatchStatsSources.Emby,
                    "jellyfin" => WatchStatsSources.Jellyfin,
                    _ => null
                };
                if (key is not null && !list.Contains(key))
                    list.Add(key);
            }
        }

        if (!list.Contains(WatchStatsSources.Emby) && embyReporting.IsConfigured)
            list.Add(WatchStatsSources.Emby);
        if (!list.Contains(WatchStatsSources.Jellyfin) && jellyfinReporting.IsConfigured)
            list.Add(WatchStatsSources.Jellyfin);

        var trakt = await traktAccounts.ListAccountsAsync(ct);
        if (trakt.Count > 0)
            list.Add(WatchStatsSources.Trakt);

        return list;
    }

    public bool IsTautulliConfigured => tautulli.IsConfigured;
    public bool IsTracearrConfigured => tracearr.IsConfigured;

    private async Task<IReadOnlyList<TracearrServerInfo>> GetServersAsync(CancellationToken ct)
    {
        if (_servers is not null && DateTimeOffset.UtcNow - _serversCachedAt < TimeSpan.FromMinutes(5))
            return _servers;

        _servers = await tracearr.GetServersAsync(ct);
        _serversCachedAt = DateTimeOffset.UtcNow;
        return _servers;
    }
}
