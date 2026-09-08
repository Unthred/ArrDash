using ArrDash.Models;
using ArrDash.Services;
using System.Text.Json;

namespace ArrDash.Tests.Services;

public sealed class NetworkBandwidthBuilderTests
{
    [Theory]
    [InlineData(2000, 1000, 1, 1000)]
    [InlineData(1000, 2000, 1, 0)]
    [InlineData(1500, 1000, 2, 250)]
    public void ComputeRate_CalculatesPositiveDelta(long current, long previous, double elapsed, long expected) =>
        Assert.Equal(expected, NetworkBandwidthBuilder.ComputeRate(current, previous, elapsed));

    [Fact]
    public void Map_MatchesKnownContainerNames()
    {
        var (key, label) = ContainerNetworkMapper.Map("binhex-qbittorrent");
        Assert.Equal("qbittorrent", key);
        Assert.Equal("qBittorrent", label);
    }

    [Fact]
    public void Build_Upload_UsesOnlyObservedInternetTraffic()
    {
        var containers = new List<ContainerNetworkRate>
        {
            new("plex", 1000, 5000),
            new("slskd", 2000, 3000)
        };

        var detail = NetworkBandwidthBuilder.Build(
            NetworkBandwidthDirection.Upload,
            totalBytesPerSecond: 2_000_000,
            DateTimeOffset.UtcNow,
            containers,
            new Dictionary<string, string?>(),
            null);

        Assert.Contains(detail.Rows, r => r.Key == "plex" && r.Source == "container" && r.BytesPerSecond == 5000);
        Assert.Contains(detail.Rows, r => r.Key == "slskd");
        Assert.Contains(detail.Rows, r => r.Key == "unattributed");
    }

    [Fact]
    public void Build_AssignsContainerCpu_WhenContainerIsKnown()
    {
        var detail = NetworkBandwidthBuilder.Build(
            NetworkBandwidthDirection.Download,
            totalBytesPerSecond: 1_000,
            DateTimeOffset.UtcNow,
            [new ContainerNetworkRate("binhex-qbittorrent", 1_000, 0)],
            new Dictionary<string, string?>(),
            null,
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["binhex-qbittorrent"] = 12.5
            });

        var row = Assert.Single(detail.Rows);
        Assert.Equal("qbittorrent", row.Key);
        Assert.Equal(12.5, row.CpuPercent);
    }

    [Fact]
    public void ParseContainerNetworkBytes_SumsInterfaces()
    {
        using var doc = JsonDocument.Parse("""
            {
              "networks": {
                "eth0": { "rx_bytes": 100, "tx_bytes": 200 },
                "eth1": { "rx_bytes": 50, "tx_bytes": 25 }
              }
            }
            """);

        var (rx, tx) = UnraidActivityService.ParseContainerNetworkBytes(doc.RootElement);
        Assert.Equal(150, rx);
        Assert.Equal(225, tx);
    }
}
