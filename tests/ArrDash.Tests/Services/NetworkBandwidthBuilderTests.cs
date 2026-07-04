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
    public void Build_Upload_PrefersStreamingSessionsOverDockerPlex()
    {
        var sessions = new List<ActiveSession>
        {
            new(
                "1",
                StreamingServer.Plex,
                "User",
                "Movie Title",
                null,
                "movie",
                50,
                null,
                null,
                null,
                IsLocal: false,
                BitrateKbps: 8000,
                BandwidthKbps: 8000)
        };

        var containers = new List<ContainerNetworkRate>
        {
            new("plex", 1000, 5000),
            new("slskd", 2000, 3000)
        };

        var detail = NetworkBandwidthBuilder.Build(
            NetworkBandwidthDirection.Upload,
            totalBytesPerSecond: 2_000_000,
            DateTimeOffset.UtcNow,
            sessions,
            containers,
            new Dictionary<string, string?>(),
            null);

        Assert.Contains(detail.Rows, r => r.Key == "plex" && r.Source == "session");
        Assert.DoesNotContain(detail.Rows, r => r.Key == "plex" && r.Source == "container");
        Assert.Contains(detail.Rows, r => r.Key == "slskd");
        Assert.Contains(detail.Rows, r => r.Key == "unattributed");
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
