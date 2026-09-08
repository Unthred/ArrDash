using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>Supplies a network total and, when possible, per-container attribution.</summary>
public interface INetworkTrafficProvider
{
    NetworkTrafficSnapshot GetLatest();
}

public sealed record NetworkTrafficSnapshot(
    NetworkThroughput? Total,
    IReadOnlyList<ContainerNetworkRate> Rates,
    string? Note,
    string Provider,
    string Attribution);

/// <summary>Portable default: host and Docker counters, which can include LAN traffic.</summary>
public sealed class DockerTrafficProvider(
    HostNetworkSamplerService hostNetwork,
    ContainerNetworkSamplerService containers) : INetworkTrafficProvider
{
    public NetworkTrafficSnapshot GetLatest()
    {
        var latest = containers.GetLatest();
        return new(hostNetwork.GetLatest(), latest.Rates, latest.Note, "docker", "Container traffic (may include LAN)");
    }
}

/// <summary>Selects the configured optional telemetry source; Docker is always the safe default.</summary>
public sealed class NetworkTrafficProviderSelector(
    DockerTrafficProvider docker,
    OpnsenseTrafficSamplerService opnsense) : INetworkTrafficProvider
{
    public NetworkTrafficSnapshot GetLatest() =>
        IsOpnsenseSelected(Environment.GetEnvironmentVariable("ARRDASH_NETWORK_PROVIDER"))
            ? opnsense.GetLatest()
            : docker.GetLatest();

    internal static bool IsOpnsenseSelected(string? configuredProvider) =>
        string.Equals(configuredProvider, "opnsense", StringComparison.OrdinalIgnoreCase);
}
