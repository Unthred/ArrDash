using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Samples the host's network interface throughput on its own background loop, independent of
/// any caller. Previously this delta was computed against a single shared "previous sample"
/// field on UnraidActivityService, read by at least three uncoordinated consumers (the ambient
/// metrics poll, the status-bar pill poll, and the on-demand bandwidth breakdown panel) -- each
/// call clobbered the baseline for every other caller, so whichever one fired right after
/// another measured a near-instant, noise-dominated window instead of a real rate. That's how
/// the breakdown panel could show "0 B/s" total while its own per-container rows (sourced from
/// ContainerNetworkSamplerService's independently-clocked cache) summed to several MB/s. Same
/// fix as that service: one steady clock, everyone reads the cached result.
/// </summary>
public sealed class HostNetworkSamplerService : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly string _procNetDevPath;
    private readonly string _networkInterface;
    private readonly ILogger<HostNetworkSamplerService> _logger;

    private readonly object _lock = new();
    private NetworkThroughput? _latest;
    private (long Rx, long Tx, DateTimeOffset At)? _prevSample;

    public HostNetworkSamplerService(ILogger<HostNetworkSamplerService> logger)
    {
        _logger = logger;
        _procNetDevPath = Environment.GetEnvironmentVariable("ARRDASH_HOST_PROC_NET_DEV") ?? "/host/proc/1/net/dev";
        _networkInterface = Environment.GetEnvironmentVariable("ARRDASH_NET_INTERFACE") ?? "br0";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        do
        {
            Sample();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Sample()
    {
        try
        {
            var current = UnraidActivityService.ReadNetworkBytes(_procNetDevPath, _networkInterface, _logger);
            if (current is null)
                return;

            var now = DateTimeOffset.UtcNow;
            lock (_lock)
            {
                if (_prevSample is not { } prev)
                {
                    _prevSample = (current.Value.Rx, current.Value.Tx, now);
                    return;
                }

                var elapsedSeconds = (now - prev.At).TotalSeconds;
                var rxDelta = current.Value.Rx - prev.Rx;
                var txDelta = current.Value.Tx - prev.Tx;
                _prevSample = (current.Value.Rx, current.Value.Tx, now);

                if (elapsedSeconds > 0 && rxDelta >= 0 && txDelta >= 0)
                    _latest = new NetworkThroughput((long)(rxDelta / elapsedSeconds), (long)(txDelta / elapsedSeconds));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sample host network throughput");
        }
    }

    public NetworkThroughput? GetLatest()
    {
        lock (_lock)
            return _latest;
    }
}
