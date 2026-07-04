using ArrDash.Models;
using Microsoft.Extensions.Hosting;

namespace ArrDash.Services;

/// <summary>
/// Samples CPU usage on its own background loop, independent of any browser page,
/// so the CPU graph has real history the moment a page opens rather than starting empty.
/// </summary>
public sealed class CpuHistoryService(HostSystemMetricsService hostMetrics) : BackgroundService
{
    private static readonly TimeSpan MaxRetention = TimeSpan.FromMinutes(240);

    private readonly object _lock = new();
    private readonly List<CpuSample> _samples = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("ARRDASH_METRICS_POLL_SECONDS"), out var s)
            ? Math.Clamp(s, 1, 60)
            : 2;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            Sample();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Sample()
    {
        var (percent, ioWait) = hostMetrics.ReadCpuPercent();
        if (percent is not double busy || ioWait is not double waiting)
            return;

        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            _samples.Add(new CpuSample(now, Math.Clamp(busy, 0, 100), Math.Clamp(waiting, 0, 100)));
            _samples.RemoveAll(s => s.At < now - MaxRetention);
        }
    }

    public IReadOnlyList<CpuSample> GetSamples(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        lock (_lock)
            return _samples.Where(s => s.At >= cutoff).OrderBy(s => s.At).ToList();
    }
}
