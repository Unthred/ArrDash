using ArrDash.Services;
using ArrDash.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArrDash.Tests.Services;

// Deliberately never calls LoadAsync/SaveAsync, so unlike LayoutPreferencesServiceTests this
// never touches ARRDASH_CONFIG_PATH or disk -- that env var is process-wide and xUnit runs test
// classes in parallel by default, so mutating it here would race with any other test doing the
// same and could corrupt both.
public class PanelRefreshSubscriptionTests
{
    private readonly LayoutPreferencesService _prefs =
        new(new FakeWebHostEnvironment(), NullLogger<LayoutPreferencesService>.Instance);
    private readonly DashboardState _dashState = new();
    private readonly List<bool> _refreshCalls = [];

    [Fact]
    public void Routine_dashboard_refresh_does_not_force()
    {
        using var subscription = new PanelRefreshSubscription(_prefs, _dashState, force => _refreshCalls.Add(force));

        // A regular ~30s automatic poll tick calling DashboardState.Update must never force a
        // subscribed panel to bypass its own cache -- that's the exact bug this class exists to
        // prevent (it previously hammered Sonarr/Radarr/Chaptarr/ABS every poll tick).
        _dashState.Update(DashboardState.Empty());

        Assert.Contains(false, _refreshCalls);
        Assert.DoesNotContain(true, _refreshCalls);
    }

    [Fact]
    public void Preference_change_does_not_force()
    {
        using var subscription = new PanelRefreshSubscription(_prefs, _dashState, force => _refreshCalls.Add(force));

        _prefs.SetPreview(_prefs.Current);

        Assert.Contains(false, _refreshCalls);
        Assert.DoesNotContain(true, _refreshCalls);
    }

    [Fact]
    public void Manual_refresh_request_forces()
    {
        using var subscription = new PanelRefreshSubscription(_prefs, _dashState, force => _refreshCalls.Add(force));

        _dashState.NotifyManualRefresh();

        Assert.Contains(true, _refreshCalls);
    }

    [Fact]
    public void Disposed_subscription_stops_receiving_events()
    {
        var subscription = new PanelRefreshSubscription(_prefs, _dashState, force => _refreshCalls.Add(force));
        subscription.Dispose();

        _dashState.Update(DashboardState.Empty());
        _dashState.NotifyManualRefresh();
        _prefs.SetPreview(_prefs.Current);

        Assert.Empty(_refreshCalls);
    }
}
