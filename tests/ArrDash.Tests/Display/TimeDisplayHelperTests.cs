using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Display;

public sealed class TimeDisplayHelperTests
{
    [Fact]
    public void FormatLastRefresh_relative_uses_seconds_not_stuck_on_just_now()
    {
        var ts = DateTimeOffset.UtcNow.AddSeconds(-42);
        var label = TimeDisplayHelper.FormatLastRefresh(ts, TimeDisplayFormat.Relative);
        Assert.Equal("42s ago", label);
    }

    [Fact]
    public void FormatLastRefresh_relative_stays_just_now_only_for_fresh_poll()
    {
        var ts = DateTimeOffset.UtcNow.AddSeconds(-2);
        var label = TimeDisplayHelper.FormatLastRefresh(ts, TimeDisplayFormat.Relative);
        Assert.Equal("just now", label);
    }

    [Fact]
    public void FormatRelative_still_uses_just_now_for_item_timestamps()
    {
        var ts = DateTimeOffset.UtcNow.AddSeconds(-42);
        var label = TimeDisplayHelper.FormatRelative(ts);
        Assert.Equal("just now", label);
    }
}
