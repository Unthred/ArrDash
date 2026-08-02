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

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1_000, "0:01")]
    [InlineData(65_000, "1:05")]
    [InlineData(3_723_000, "1:02:03")]
    public void FormatPlayback_formats_ms_as_m_ss_or_h_mm_ss(long ms, string expected)
    {
        Assert.Equal(expected, TimeDisplayHelper.FormatPlayback(ms));
    }
}
