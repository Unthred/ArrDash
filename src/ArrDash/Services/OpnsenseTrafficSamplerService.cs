using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Samples WAN counters and live PF states at the router. This is necessary for Docker macvlan
/// networks, whose traffic does not traverse the Unraid host's conntrack table.
/// </summary>
public sealed class OpnsenseTrafficSamplerService(
    OpenBaoSecretsClient openBao,
    ILogger<OpnsenseTrafficSamplerService> logger) : BackgroundService
{
    // One light stats request and one bounded (500-row) PF query. Five seconds keeps the
    // drilldown responsive without polling the router's full state table.
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);
    private const string PlexPort = "32400";
    private readonly object _lock = new();
    private (long Rx, long Tx, DateTimeOffset At)? _previousWan;
    private NetworkThroughput? _wan;
    private IReadOnlyList<ContainerNetworkRate> _rates = [];
    // Preserve discovered addresses when Docker briefly omits a macvlan attachment during a
    // restart. Operators can add host-network/custom apps through ARRDASH_NETWORK_ADDRESS_MAP.
    private readonly Dictionary<string, string> _knownContainerAddresses = new(StringComparer.Ordinal);
    private string? _note = "Connecting to router telemetry…";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        do
        {
            await SampleAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        try
        {
            if (!IsEnabled)
            {
                Set([], null, "OPNsense router telemetry is disabled.");
                return;
            }

            var credentials = await openBao.ReadOpnsenseAsync(ct);
            if (credentials is null or { IsComplete: false })
            {
                Set([], null, "Router telemetry is not configured.");
                return;
            }

            var addresses = RememberContainerAddresses(await FetchContainerAddressesAsync(ct));
            var hostAddresses = FetchHostAddresses(addresses);
            using var client = CreateRouterClient(credentials);
            using var stats = await client.GetAsync("api/diagnostics/firewall/pf_statistics/interfaces", ct);
            using var flows = await client.PostAsync(
                "api/diagnostics/firewall/query_pf_top",
                new StringContent("{\"rowCount\":500,\"current\":1,\"sort\":{\"avg\":\"desc\"}}", Encoding.UTF8, "application/json"),
                ct);
            using var arp = await client.GetAsync("api/diagnostics/interface/get_arp", ct);
            stats.EnsureSuccessStatusCode();
            flows.EnsureSuccessStatusCode();
            arp.EnsureSuccessStatusCode();

            using var statsJson = JsonDocument.Parse(await stats.Content.ReadAsStringAsync(ct));
            using var flowsJson = JsonDocument.Parse(await flows.Content.ReadAsStringAsync(ct));
            using var arpJson = JsonDocument.Parse(await arp.Content.ReadAsStringAsync(ct));
            var iface = GetWanInterface(statsJson.RootElement, credentials.WanInterface);
            var now = DateTimeOffset.UtcNow;
            var wanRx = iface.GetProperty("in4_pass_bytes").GetInt64();
            var wanTx = iface.GetProperty("out4_pass_bytes").GetInt64();
            var currentFlows = ParseFlows(flowsJson.RootElement, addresses, hostAddresses, ParseLanDevices(arpJson.RootElement));

            lock (_lock)
            {
                if (_previousWan is { } previousWan)
                {
                    var elapsed = (now - previousWan.At).TotalSeconds;
                    _wan = new NetworkThroughput(
                        NetworkBandwidthBuilder.ComputeRate(wanRx, previousWan.Rx, elapsed),
                        NetworkBandwidthBuilder.ComputeRate(wanTx, previousWan.Tx, elapsed));
                }

                _previousWan = (wanRx, wanTx, now);
                _rates = CalculateContainerRates(currentFlows);

                _note = _wan is null ? "Collecting router baseline…" : null;
            }
        }
        catch (OperationCanceledException)
        {
            // Service is stopping.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sample OPNsense traffic");
            Set([], null, "Router telemetry is temporarily unavailable.");
        }
    }

    private static bool IsEnabled => string.Equals(
        Environment.GetEnvironmentVariable("ARRDASH_NETWORK_PROVIDER"), "opnsense", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateRouterClient(OpnsenseCredentials credentials)
    {
        HttpMessageHandler handler;
        if (IPAddress.TryParse(credentials.ConnectAddress, out var connectAddress))
        {
            handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, ct) =>
                {
                    var socket = new Socket(connectAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(new IPEndPoint(connectAddress, context.DnsEndPoint.Port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };
        }
        else
        {
            handler = new SocketsHttpHandler();
        }

        var client = new HttpClient(handler) { BaseAddress = new Uri(credentials.Url!.TrimEnd('/') + "/") };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        return client;
    }

    private static async Task<IReadOnlyDictionary<string, string>> FetchContainerAddressesAsync(CancellationToken ct)
    {
        const string socketPath = "/var/run/docker.sock";
        if (!File.Exists(socketPath))
            return new Dictionary<string, string>();

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://docker") };
        using var response = await client.GetAsync("containers/json", ct);
        if (!response.IsSuccessStatusCode)
            return new Dictionary<string, string>();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseContainerAddresses(document.RootElement);
    }

    private IReadOnlyDictionary<string, string> RememberContainerAddresses(IReadOnlyDictionary<string, string> discovered)
    {
        lock (_lock)
        {
            foreach (var pair in discovered)
                _knownContainerAddresses[pair.Key] = pair.Value;
            foreach (var pair in ParseManualAddressMappings(Environment.GetEnvironmentVariable("ARRDASH_NETWORK_ADDRESS_MAP")))
                _knownContainerAddresses[pair.Key] = pair.Value;
            return new Dictionary<string, string>(_knownContainerAddresses, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Parses <c>app=ip,app2=ip2</c> mappings for host-networked or externally managed apps.
    /// Docker-discovered addresses remain automatic; explicit mappings take precedence.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ParseManualAddressMappings(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && IPAddress.TryParse(parts[1], out _))
                result[parts[1]] = parts[0];
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, string> ParseLanDevices(JsonElement devices)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (devices.ValueKind != JsonValueKind.Array) return result;
        foreach (var device in devices.EnumerateArray())
        {
            var ip = GetString(device, "ip");
            if (!IPAddress.TryParse(ip, out _)) continue;
            var hostname = GetString(device, "hostname");
            var maker = GetString(device, "manufacturer");
            var network = GetString(device, "intf_description");
            var label = !string.IsNullOrWhiteSpace(hostname) ? hostname : !string.IsNullOrWhiteSpace(maker) ? maker : "Unknown LAN device";
            result[ip] = string.IsNullOrWhiteSpace(network) ? label : $"{label} — {network}";
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, string> ParseContainerAddresses(JsonElement containers)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (containers.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var container in containers.EnumerateArray())
        {
            if (!container.TryGetProperty("Names", out var names) || names.GetArrayLength() == 0 ||
                !container.TryGetProperty("NetworkSettings", out var settings) ||
                !settings.TryGetProperty("Networks", out var networks))
                continue;

            var name = names[0].GetString()?.TrimStart('/');
            if (string.IsNullOrWhiteSpace(name))
                continue;

            foreach (var network in networks.EnumerateObject())
                if (network.Value.TryGetProperty("IPAddress", out var address) && !string.IsNullOrWhiteSpace(address.GetString()))
                    result[address.GetString()!] = name;
        }
        return result;
    }

    // Host-network containers share Unraid's address, so they cannot be discovered from the
    // Docker address map. fib_trie exposes the host's local IPv4 addresses without needing a
    // shell tool in the application image. Docker bridge addresses are excluded below.
    private static IReadOnlySet<string> FetchHostAddresses(IReadOnlyDictionary<string, string> containerAddresses)
    {
        var result = (Environment.GetEnvironmentVariable("ARRDASH_HOST_LAN_IP") ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(address => !containerAddresses.ContainsKey(address) && IsPrivateHostAddress(address))
            .ToHashSet(StringComparer.Ordinal);

        const string fibTriePath = "/host/proc/1/net/fib_trie";
        if (!File.Exists(fibTriePath))
            return result;

        try
        {
            var content = File.ReadAllText(fibTriePath);
            result.UnionWith(Regex.Matches(content, @"\|--[ \t]+(?<address>\d{1,3}(?:\.\d{1,3}){3})[ \t]*\r?\n[ \t]*/32 host LOCAL")
                .Select(match => match.Groups["address"].Value)
                .Where(address => !containerAddresses.ContainsKey(address) && IsPrivateHostAddress(address)));
            return result;
        }
        catch (IOException)
        {
            return result;
        }
    }

    internal static JsonElement GetWanInterface(JsonElement response, string interfaceName)
    {
        var interfaces = response.GetProperty("interfaces");
        if (interfaces.ValueKind == JsonValueKind.Object)
            return interfaces.GetProperty(interfaceName);

        foreach (var item in interfaces.EnumerateArray())
            if (item.TryGetProperty(interfaceName, out var iface))
                return iface;

        throw new KeyNotFoundException($"WAN interface '{interfaceName}' was not returned by the router.");
    }

    // PF exposes both directions of a state. The inbound view is one unambiguous copy; count
    // it once and classify the direction from the container endpoint and its public peer.
    internal static IReadOnlyDictionary<string, FlowCounters> ParseFlows(
        JsonElement response,
        IReadOnlyDictionary<string, string> containerNamesByAddress,
        IReadOnlySet<string>? hostAddresses = null,
        IReadOnlyDictionary<string, string>? lanDevices = null)
    {
        var result = new Dictionary<string, FlowCounters>(StringComparer.Ordinal);
        if (!response.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("dir", out var direction) || direction.GetString() != "in" ||
                !row.TryGetProperty("src_addr", out var source) || !row.TryGetProperty("dst_addr", out var destination) ||
                !row.TryGetProperty("avg", out var averageProperty) || !averageProperty.TryGetDouble(out var average))
                continue;

            var src = source.GetString();
            var dst = destination.GetString();
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
                continue;

            var flowKey = $"{src}:{GetString(row, "src_port")}>{dst}:{GetString(row, "dst_port")}";
            var bytesPerSecond = Math.Max(0, (long)average);
            if (containerNamesByAddress.TryGetValue(src, out var uploadContainer) && IsPublicAddress(dst))
            {
                var download = IsLikelyServerResponse(row);
                result[flowKey] = new FlowCounters(uploadContainer, download ? bytesPerSecond : 0, download ? 0 : bytesPerSecond);
            }
            else if (containerNamesByAddress.TryGetValue(dst, out var downloadContainer) && IsPublicAddress(src))
                result[flowKey] = new FlowCounters(downloadContainer, bytesPerSecond, 0);
            else if (hostAddresses is not null && IsPlexFlow(src, dst, row, hostAddresses))
            {
                // PlexMediaServer uses Docker host networking. Its remote stream is identified
                // by Plex's HTTPS service port on the shared Unraid address.
                var upload = hostAddresses.Contains(src);
                result[flowKey] = new FlowCounters("PlexMediaServer", upload ? 0 : bytesPerSecond, upload ? bytesPerSecond : 0);
            }
            else if (IsPrivateHostAddress(src) && IsPublicAddress(dst))
            {
                // Keep genuine WAN flows visible even when the LAN endpoint is not a Docker
                // attachment (a host-network process, VM, or unmanaged device).
                result[flowKey] = new FlowCounters(LanLabel(src, hostAddresses, lanDevices), 0, bytesPerSecond);
            }
            else if (IsPrivateHostAddress(dst) && IsPublicAddress(src))
            {
                result[flowKey] = new FlowCounters(LanLabel(dst, hostAddresses, lanDevices), bytesPerSecond, 0);
            }
        }
        return result;
    }

    internal static IReadOnlyList<ContainerNetworkRate> CalculateContainerRates(
        IReadOnlyDictionary<string, FlowCounters> current)
    {
        var totals = new Dictionary<string, (long Rx, long Tx)>(StringComparer.Ordinal);
        foreach (var flow in current.Values)
        {
            totals.TryGetValue(flow.Container, out var total);
            totals[flow.Container] = (total.Rx + flow.Rx, total.Tx + flow.Tx);
        }

        return totals.Select(pair => new ContainerNetworkRate(pair.Key, pair.Value.Rx, pair.Value.Tx)).ToList();
    }

    private static string GetString(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) ? value.ToString() : "";

    private static string LanLabel(string ip, IReadOnlySet<string>? hosts, IReadOnlyDictionary<string, string>? devices) =>
        hosts?.Contains(ip) == true ? "Unraid host" : devices?.TryGetValue(ip, out var label) == true ? $"{label} ({ip})" : $"Unknown LAN device ({ip})";

    // PF exposes the initiating tuple, rather than directional byte counters. A local ephemeral
    // port connected to a remote well-known service (for example NNTP-over-TLS on 563) is a
    // download even though its LAN endpoint is the tuple source.
    private static bool IsLikelyServerResponse(JsonElement row) =>
        int.TryParse(GetString(row, "src_port"), out var localPort) &&
        int.TryParse(GetString(row, "dst_port"), out var remotePort) &&
        localPort > 1024 && remotePort is > 0 and <= 1024;

    private static bool IsPlexFlow(string src, string dst, JsonElement row, IReadOnlySet<string> hostAddresses) =>
        // The container-IP branches above have already claimed all addressable Docker traffic.
        // What remains on Plex's public service port is its host-network traffic, even if the
        // host address cannot be discovered in a constrained container environment.
        (IsPublicAddress(src) && GetString(row, "dst_port") == PlexPort) ||
        (IsPublicAddress(dst) && GetString(row, "src_port") == PlexPort) ||
        (hostAddresses.Contains(src) && IsPublicAddress(dst) && GetString(row, "src_port") == PlexPort) ||
        (hostAddresses.Contains(dst) && IsPublicAddress(src) && GetString(row, "dst_port") == PlexPort);

    private static bool IsPrivateHostAddress(string value) =>
        IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork &&
        !IPAddress.IsLoopback(address) && !IsPublicAddress(value);

    private static bool IsPublicAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 or 127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] is >= 16 and <= 31 => false,
            192 when bytes[1] == 168 => false,
            _ => true
        };
    }

    private void Set(IReadOnlyList<ContainerNetworkRate> rates, NetworkThroughput? wan, string? note)
    {
        lock (_lock)
        {
            _rates = rates;
            _wan = wan;
            _note = note;
        }
    }

    public NetworkTrafficSnapshot GetLatest()
    {
        lock (_lock)
            return new(_wan, _rates, _note, "opnsense", "WAN PF-flow attribution");
    }

    internal sealed record FlowCounters(string Container, long Rx, long Tx);
}
