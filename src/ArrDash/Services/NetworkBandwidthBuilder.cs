using ArrDash.Models;

namespace ArrDash.Services;

public static class NetworkBandwidthBuilder
{
    public static long BytesPerSecondForDirection(ContainerNetworkRate rate, NetworkBandwidthDirection direction) =>
        direction == NetworkBandwidthDirection.Download
            ? rate.RxBytesPerSecond
            : rate.TxBytesPerSecond;

    public static NetworkBandwidthDetail Build(
        NetworkBandwidthDirection direction,
        long totalBytesPerSecond,
        DateTimeOffset sampledAt,
        IReadOnlyList<ContainerNetworkRate> containerRates,
        IReadOnlyDictionary<string, string?> serviceUrls,
        string? note,
        IReadOnlyDictionary<string, double>? cpuByContainerName = null,
        string provider = "docker",
        string attribution = "Container traffic (may include LAN)")
    {
        var rows = new Dictionary<string, MutableRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var rate in containerRates)
        {
            var bps = BytesPerSecondForDirection(rate, direction);
            if (bps <= 0)
                continue;

            var (key, label) = ContainerNetworkMapper.Map(rate.ContainerName);
            var cpu = cpuByContainerName?.GetValueOrDefault(rate.ContainerName);

            if (!rows.TryGetValue(key, out var row))
            {
                rows[key] = new MutableRow(
                    key,
                    label,
                    bps,
                    "container",
                    serviceUrls.GetValueOrDefault(key),
                    [new NetworkBandwidthDetailItem(rate.ContainerName, "Container traffic")])
                {
                    CpuPercent = cpu
                };
                continue;
            }

            row.BytesPerSecond += bps;
            row.CpuPercent = (row.CpuPercent ?? 0) + (cpu ?? 0);
            row.DetailItems.Add(new NetworkBandwidthDetailItem(rate.ContainerName, "Container traffic"));
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
            note,
            provider,
            attribution);
    }

    public static long ComputeRate(long currentBytes, long previousBytes, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0)
            return 0;

        var delta = currentBytes - previousBytes;
        return delta < 0 ? 0 : (long)(delta / elapsedSeconds);
    }

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
