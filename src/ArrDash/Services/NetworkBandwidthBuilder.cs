using ArrDash.Models;

namespace ArrDash.Services;

public static class NetworkBandwidthBuilder
{
    public static long BytesPerSecondForDirection(ContainerNetworkRate rate, NetworkBandwidthDirection direction) =>
        direction == NetworkBandwidthDirection.Download
            ? rate.RxBytesPerSecond
            : rate.TxBytesPerSecond;

    public static long SessionBytesPerSecond(int? bandwidthKbps) =>
        bandwidthKbps is int kb and > 0 ? (long)(kb * 1000.0 / 8.0) : 0;

    public static NetworkBandwidthDetail Build(
        NetworkBandwidthDirection direction,
        long totalBytesPerSecond,
        DateTimeOffset sampledAt,
        IReadOnlyList<ActiveSession> sessions,
        IReadOnlyList<ContainerNetworkRate> containerRates,
        IReadOnlyDictionary<string, string?> serviceUrls,
        string? note,
        IReadOnlyDictionary<string, double>? cpuByContainerName = null)
    {
        var rows = new Dictionary<string, MutableRow>(StringComparer.OrdinalIgnoreCase);
        var claimedStreamingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (direction == NetworkBandwidthDirection.Upload)
            AddStreamingRows(rows, sessions, serviceUrls, claimedStreamingKeys);

        foreach (var rate in containerRates)
        {
            var bps = BytesPerSecondForDirection(rate, direction);
            if (bps <= 0)
                continue;

            var (key, label) = ContainerNetworkMapper.Map(rate.ContainerName);
            if (claimedStreamingKeys.Contains(key))
                continue;

            var cpu = cpuByContainerName?.GetValueOrDefault(rate.ContainerName);

            if (!rows.TryGetValue(key, out var row))
            {
                rows[key] = new MutableRow(
                    key,
                    label,
                    bps,
                    "container",
                    serviceUrls.GetValueOrDefault(key),
                    [new NetworkBandwidthDetailItem(rate.ContainerName, "Container I/O")])
                {
                    CpuPercent = cpu
                };
                continue;
            }

            row.BytesPerSecond += bps;
            row.CpuPercent = (row.CpuPercent ?? 0) + (cpu ?? 0);
            row.DetailItems.Add(new NetworkBandwidthDetailItem(rate.ContainerName, "Container I/O"));
        }

        var ordered = rows.Values
            .Where(r => r.BytesPerSecond > 0)
            .OrderByDescending(r => r.BytesPerSecond)
            .ToList();

        var attributed = ordered.Sum(r => r.BytesPerSecond);
        var unattributed = Math.Max(0, totalBytesPerSecond - attributed);

        var resultRows = ordered
            .Select(r => new NetworkBandwidthRow(
                r.Key,
                r.Label,
                r.BytesPerSecond,
                attributed > 0 ? r.BytesPerSecond * 100.0 / attributed : 0,
                r.DetailItems,
                r.ServiceUrl,
                r.Source,
                r.CpuPercent))
            .ToList();

        if (unattributed > 0)
        {
            resultRows.Add(new NetworkBandwidthRow(
                "unattributed",
                "Unattributed / other",
                unattributed,
                attributed + unattributed > 0 ? unattributed * 100.0 / (attributed + unattributed) : 0,
                [new NetworkBandwidthDetailItem("Host traffic, VPN, or unmonitored apps", null)],
                null,
                "remainder"));
        }

        return new NetworkBandwidthDetail(
            direction,
            totalBytesPerSecond,
            attributed,
            unattributed,
            sampledAt,
            resultRows,
            note);
    }

    public static long ComputeRate(long currentBytes, long previousBytes, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
            return 0;

        var delta = currentBytes - previousBytes;
        return delta < 0 ? 0 : (long)(delta / elapsedSeconds);
    }

    private static void AddStreamingRows(
        Dictionary<string, MutableRow> rows,
        IReadOnlyList<ActiveSession> sessions,
        IReadOnlyDictionary<string, string?> serviceUrls,
        HashSet<string> claimedStreamingKeys)
    {
        foreach (var group in sessions
                     .Where(s => SessionBytesPerSecond(s.BandwidthKbps ?? s.BitrateKbps) > 0)
                     .GroupBy(SessionServerKey))
        {
            var key = group.Key;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var label = group.First().Server.ToString();
            var bytesPerSecond = group.Sum(s => SessionBytesPerSecond(s.BandwidthKbps ?? s.BitrateKbps));
            if (bytesPerSecond <= 0)
                continue;

            claimedStreamingKeys.Add(key);
            rows[key] = new MutableRow(
                key,
                label,
                bytesPerSecond,
                "session",
                serviceUrls.GetValueOrDefault(key),
                group.Select(s => new NetworkBandwidthDetailItem(
                    s.Title,
                    BuildSessionSubtitle(s))).ToList());
        }
    }

    private static string? BuildSessionSubtitle(ActiveSession session)
    {
        var parts = new List<string>();
        if (session.IsLocal is bool local)
            parts.Add(local ? "LAN" : "WAN");
        if (session.BandwidthKbps is int bw)
            parts.Add(BitrateDisplayHelper.Format(bw));
        return parts.Count > 0 ? string.Join(" · ", parts) : session.User;
    }

    private static string SessionServerKey(ActiveSession session) => session.Server switch
    {
        StreamingServer.Plex => "plex",
        StreamingServer.Emby => "emby",
        StreamingServer.Jellyfin => "jellyfin",
        _ => string.Empty
    };

    private sealed class MutableRow(
        string key,
        string label,
        long bytesPerSecond,
        string source,
        string? serviceUrl,
        List<NetworkBandwidthDetailItem> detailItems)
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        public long BytesPerSecond { get; set; } = bytesPerSecond;
        public string Source { get; } = source;
        public string? ServiceUrl { get; } = serviceUrl;
        public List<NetworkBandwidthDetailItem> DetailItems { get; } = detailItems;
        public double? CpuPercent { get; set; }
    }
}

public sealed record ContainerNetworkRate(string ContainerName, long RxBytesPerSecond, long TxBytesPerSecond);

public sealed record ContainerNetworkSample(string ContainerName, long RxBytes, long TxBytes, DateTimeOffset SampledAt);
