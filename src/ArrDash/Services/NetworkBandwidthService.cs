using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed class NetworkBandwidthService(
    UnraidActivityService unraidActivity,
    ContainerNetworkSamplerService containerSampler,
    HostNetworkSamplerService hostNetworkSampler,
    MediaServiceOptionsAccessor options)
{
    public async Task<NetworkBandwidthDetail> FetchDetailAsync(
        NetworkBandwidthDirection direction,
        IReadOnlyList<ActiveSession> sessions,
        CancellationToken ct)
    {
        // Both rates come from their own background-sampled caches, not a live read -- reading
        // the host interface counter on demand raced with every other caller (ambient metrics
        // poll, status-bar pill poll) over a single shared "previous sample" baseline, so
        // whichever one fired right after another measured a near-instant, noise-dominated
        // window instead of a real rate (this is how the total could read ~0 B/s while the
        // per-container rows below summed to several MB/s). Container rates were already fixed
        // this way earlier -- this host runs 70+ containers and a live sample takes 60-140+ seconds.
        var total = hostNetworkSampler.GetLatest();
        var (containerRates, note, _) = containerSampler.GetLatest();
        var totalBytesPerSecond = direction == NetworkBandwidthDirection.Download
            ? total?.RxBytesPerSecond ?? 0
            : total?.TxBytesPerSecond ?? 0;

        // Reuses UnraidActivityService's own 20s cache (the same data behind the Top CPU tile)
        // rather than sampling docker stats again here.
        var topContainers = await unraidActivity.GetTopContainersAsync(ct);
        var cpuByContainerName = topContainers.ToDictionary(c => c.Name, c => c.CpuPercent, StringComparer.Ordinal);

        return NetworkBandwidthBuilder.Build(
            direction,
            totalBytesPerSecond,
            DateTimeOffset.UtcNow,
            sessions,
            containerRates,
            BuildServiceUrls(),
            note,
            cpuByContainerName);
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
