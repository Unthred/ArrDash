using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed class NetworkBandwidthService(
    UnraidActivityService unraidActivity,
    MediaServiceOptionsAccessor options)
{
    public async Task<NetworkBandwidthDetail> FetchDetailAsync(
        NetworkBandwidthDirection direction,
        IReadOnlyList<ActiveSession> sessions,
        CancellationToken ct)
    {
        var totalTask = unraidActivity.ReadInterfaceThroughputAsync(ct);
        var containerTask = unraidActivity.ReadContainerNetworkRatesAsync(ct);
        await Task.WhenAll(totalTask, containerTask);

        var total = await totalTask;
        var (containerRates, note) = await containerTask;
        var totalBytesPerSecond = direction == NetworkBandwidthDirection.Download
            ? total?.RxBytesPerSecond ?? 0
            : total?.TxBytesPerSecond ?? 0;

        return NetworkBandwidthBuilder.Build(
            direction,
            totalBytesPerSecond,
            DateTimeOffset.UtcNow,
            sessions,
            containerRates,
            BuildServiceUrls(),
            note);
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
