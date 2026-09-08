using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed class NetworkBandwidthService(
    INetworkTrafficProvider trafficProvider,
    UnraidActivityService unraidActivity,
    MediaServiceOptionsAccessor options)
{
    public async Task<NetworkBandwidthDetail> FetchDetailAsync(
        NetworkBandwidthDirection direction,
        IReadOnlyList<ActiveSession> sessions,
        CancellationToken ct)
    {
        // Router WAN counters are the total. Its live PF states map public flows back to
        // container addresses, so Docker's LAN/container traffic is never included here.
        var traffic = trafficProvider.GetLatest();
        var total = traffic.Total;
        var containerRates = traffic.Rates;
        var totalBytesPerSecond = direction == NetworkBandwidthDirection.Download
            ? total?.RxBytesPerSecond ?? 0
            : total?.TxBytesPerSecond ?? 0;

        var adjustedRates = containerRates
            .Where(rate => !ContainerNetworkMapper.Map(rate.ContainerName).Key.Equals("plex", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Plex runs in Docker host mode here, so PF cannot identify it by container address.
        // For a remote session, Plex's reported streaming bandwidth is a more precise source
        // than the aggregate host PF state and avoids assigning that state to Download.
        if (direction == NetworkBandwidthDirection.Upload)
        {
            var plexBytesPerSecond = sessions
                .Where(session => session.Server == StreamingServer.Plex && session.IsLocal != true)
                .Sum(session => (long)(session.BandwidthKbps ?? session.BitrateKbps ?? 0) * 125L);
            if (plexBytesPerSecond > 0)
                adjustedRates.Add(new ContainerNetworkRate("PlexMediaServer", 0, plexBytesPerSecond));
        }

        // Reuse the activity service's short-lived Docker stats cache. This keeps the
        // bandwidth legend useful without adding another expensive docker stats pass.
        // Router-only LAN devices and the unattributed remainder deliberately have no CPU
        // value because they cannot be associated with a container.
        var topContainers = await unraidActivity.GetTopContainersAsync(ct);
        var cpuByContainerName = topContainers.ToDictionary(
            container => container.Name,
            container => container.CpuPercent,
            StringComparer.OrdinalIgnoreCase);

        return NetworkBandwidthBuilder.Build(
            direction,
            totalBytesPerSecond,
            DateTimeOffset.UtcNow,
            adjustedRates,
            BuildServiceUrls(),
            traffic.Note,
            cpuByContainerName,
            provider: traffic.Provider,
            attribution: traffic.Attribution);
    }

    private IReadOnlyDictionary<string, string?> BuildServiceUrls()
    {
        var media = options.Options;
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["plex"] = TrimUrl(media.Plex.Url),
            ["emby"] = TrimUrl(media.Emby.Url),
            ["jellyfin"] = TrimUrl(media.Jellyfin.Url),
            ["slskd"] = TrimUrl(media.Slskd.Url),
            ["audiobookshelf"] = TrimUrl(media.AudiobookShelf.Url),
            ["chaptarr"] = TrimUrl(media.Chaptarr.Url),
            ["sonarr"] = TrimUrl(media.Sonarr.Url),
            ["radarr"] = TrimUrl(media.Radarr.Url),
            ["lidarr"] = TrimUrl(media.Lidarr.Url),
        };
    }

    private static string? TrimUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.TrimEnd('/');
}
