using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed class MediaAggregatorService(
    DashboardRefreshService refresh,
    LayoutPreferencesService prefs,
    MediaServiceOptionsAccessor options,
    ILogger<MediaAggregatorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await refresh.RefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!prefs.Current.ManualRefreshOnly)
                    await refresh.RefreshAsync(stoppingToken);

                var interval = TimeSpan.FromSeconds(prefs.GetPollIntervalSeconds(options.Options.PollIntervalSeconds));
                await Task.Delay(interval, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Dashboard refresh failed");
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
