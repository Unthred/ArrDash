using System.Text.Json;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class OpnsenseTrafficSamplerServiceTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("docker", false)]
    [InlineData("OPNsense", true)]
    public void Provider_selection_only_enables_opnsense_when_explicitly_requested(string? configuredProvider, bool expected) =>
        Assert.Equal(expected, NetworkTrafficProviderSelector.IsOpnsenseSelected(configuredProvider));

    [Fact]
    public void Manual_address_mappings_support_host_network_and_custom_apps()
    {
        var result = OpnsenseTrafficSamplerService.ParseManualAddressMappings(
            "qbittorrent=192.168.13.182, custom-app=192.168.13.210, invalid=not-an-ip");

        Assert.Equal("qbittorrent", result["192.168.13.182"]);
        Assert.Equal("custom-app", result["192.168.13.210"]);
        Assert.DoesNotContain("not-an-ip", result.Keys);
    }

    [Fact]
    public void ParseContainerAddresses_ReadsAllAttachedNetworkAddresses()
    {
        using var document = JsonDocument.Parse("""
            [{ "Names": ["/chaptarr"], "NetworkSettings": { "Networks": {
              "bridge": { "IPAddress": "172.17.0.4" }, "media": { "IPAddress": "192.168.13.201" }
            }}}]
            """);
        var result = OpnsenseTrafficSamplerService.ParseContainerAddresses(document.RootElement);
        Assert.Equal("chaptarr", result["172.17.0.4"]);
        Assert.Equal("chaptarr", result["192.168.13.201"]);
    }

    [Fact]
    public void ParseFlows_AttributesOnlyPublicPeersAndKeepsDirections()
    {
        using var document = JsonDocument.Parse("""
            { "rows": [
              { "dir":"in", "src_addr":"192.168.13.182", "src_port":"50000", "dst_addr":"1.1.1.1", "dst_port":"443", "avg":100 },
              { "dir":"in", "src_addr":"8.8.8.8", "src_port":"443", "dst_addr":"192.168.13.201", "dst_port":"50001", "avg":400 },
              { "dir":"out", "src_addr":"192.168.13.182", "src_port":"50000", "dst_addr":"1.1.1.1", "dst_port":"443", "avg":100 },
              { "dir":"in", "src_addr":"192.168.13.10", "src_port":"443", "dst_addr":"192.168.13.201", "dst_port":"50001", "avg":999 }
            ] }
            """);
        var addresses = new Dictionary<string, string> { ["192.168.13.182"] = "qbittorrent", ["192.168.13.201"] = "chaptarr" };
        var result = OpnsenseTrafficSamplerService.ParseFlows(document.RootElement, addresses);
        Assert.Equal(2, result.Count);
        Assert.Contains(result.Values, flow => flow.Container == "qbittorrent" && flow.Rx == 100 && flow.Tx == 0);
        Assert.Contains(result.Values, flow => flow.Container == "chaptarr" && flow.Rx == 400 && flow.Tx == 0);
    }

    [Fact]
    public void GetWanInterface_AcceptsObjectAndArrayResponseShapes()
    {
        using var objectResponse = JsonDocument.Parse("""{ "interfaces": { "pppoe0": { "in4_pass_bytes": 7 } } }""");
        using var arrayResponse = JsonDocument.Parse("""{ "interfaces": [{ "pppoe0": { "in4_pass_bytes": 9 } }] }""");

        Assert.Equal(7, OpnsenseTrafficSamplerService.GetWanInterface(objectResponse.RootElement, "pppoe0").GetProperty("in4_pass_bytes").GetInt64());
        Assert.Equal(9, OpnsenseTrafficSamplerService.GetWanInterface(arrayResponse.RootElement, "pppoe0").GetProperty("in4_pass_bytes").GetInt64());
    }

    [Fact]
    public void ParseFlows_AttributesHostNetworkPlexByItsPublicServicePort()
    {
        using var document = JsonDocument.Parse("""
            { "rows": [{ "dir":"in", "src_addr":"192.168.13.1", "src_port":"32400", "dst_addr":"8.8.8.8", "dst_port":"50000", "avg":1000 }] }
            """);

        var result = OpnsenseTrafficSamplerService.ParseFlows(
            document.RootElement,
            new Dictionary<string, string>(),
            new HashSet<string> { "192.168.13.1" });

        var flow = Assert.Single(result.Values);
        Assert.Equal("PlexMediaServer", flow.Container);
        Assert.Equal(1000, flow.Tx);
    }

    [Fact]
    public void ParseFlows_keeps_unknown_lan_endpoints_visible()
    {
        using var document = JsonDocument.Parse("""
            { "rows": [{ "dir":"in", "src_addr":"192.168.13.250", "src_port":"50000", "dst_addr":"8.8.8.8", "dst_port":"443", "avg":1200 }] }
            """);

        var flow = Assert.Single(OpnsenseTrafficSamplerService.ParseFlows(document.RootElement, new Dictionary<string, string>()).Values);

        Assert.Equal("Unknown LAN device (192.168.13.250)", flow.Container);
        Assert.Equal(1200, flow.Tx);
    }

    [Fact]
    public void ParseFlows_classifies_local_ephemeral_to_remote_nntp_as_download()
    {
        using var document = JsonDocument.Parse("""
            { "rows": [{ "dir":"in", "src_addr":"192.168.13.180", "src_port":"51676", "dst_addr":"81.171.92.203", "dst_port":"563", "avg":1200 }] }
            """);

        var flow = Assert.Single(OpnsenseTrafficSamplerService.ParseFlows(document.RootElement,
            new Dictionary<string, string> { ["192.168.13.180"] = "nzbget" }).Values);

        Assert.Equal(1200, flow.Rx);
        Assert.Equal(0, flow.Tx);
    }

    [Fact]
    public void CalculateContainerRates_SumsLiveRatesForTheSameContainer()
    {
        var current = new Dictionary<string, OpnsenseTrafficSamplerService.FlowCounters>
        {
            ["first"] = new("qbittorrent", 700, 0), ["second"] = new("qbittorrent", 50, 0)
        };
        var rates = OpnsenseTrafficSamplerService.CalculateContainerRates(current);
        var rate = Assert.Single(rates);
        Assert.Equal("qbittorrent", rate.ContainerName);
        Assert.Equal(750, rate.RxBytesPerSecond);
    }
}
