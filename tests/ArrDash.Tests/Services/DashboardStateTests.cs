using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class DashboardStateTests
{
    [Fact]
    public void Update_fires_Changed_but_not_ManualRefreshRequested()
    {
        var state = new DashboardState();
        var changedCount = 0;
        var manualCount = 0;
        state.Changed += _ => changedCount++;
        state.ManualRefreshRequested += () => manualCount++;

        state.Update(DashboardState.Empty());

        Assert.Equal(1, changedCount);
        Assert.Equal(0, manualCount);
    }

    [Fact]
    public void NotifyManualRefresh_fires_ManualRefreshRequested_but_not_Changed()
    {
        var state = new DashboardState();
        var changedCount = 0;
        var manualCount = 0;
        state.Changed += _ => changedCount++;
        state.ManualRefreshRequested += () => manualCount++;

        state.NotifyManualRefresh();

        Assert.Equal(0, changedCount);
        Assert.Equal(1, manualCount);
    }
}
