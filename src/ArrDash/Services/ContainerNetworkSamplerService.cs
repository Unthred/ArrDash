namespace ArrDash.Services;

/// <summary>
/// Samples per-container network rates on its own background loop, independent of any UI
/// request. This host runs 70+ containers and a single `docker stats` call takes ~2s, so
/// sampling all of them live on click (as UnraidActivityService.ReadContainerNetworkRatesAsync
/// does when called directly) takes 60-140+ seconds -- unusable from a click-to-view panel.
/// Same pattern as CpuHistoryService: sample continuously, serve the latest cached result
/// instantly.
/// </summary>
public sealed class ContainerNetworkSamplerService(
    UnraidActivityService unraidActivity,
    ILogger<ContainerNetworkSamplerService> logger) : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(20);

    private readonly object _lock = new();
    private IReadOnlyList<ContainerNetworkRate> _latestRates = [];
    private string? _latestNote = "Collecting container baseline…";
    private DateTimeOffset _sampledAt;

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
            var (rates, note) = await unraidActivity.ReadContainerNetworkRatesAsync(ct);
            lock (_lock)
            {
                _latestRates = rates;
                _latestNote = note;
                _sampledAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sample container network rates");
        }
    }

    public (IReadOnlyList<ContainerNetworkRate> Rates, string? Note, DateTimeOffset SampledAt) GetLatest()
    {
        lock (_lock)
            return (_latestRates, _latestNote, _sampledAt);
    }
}
