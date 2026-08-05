using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class TraktSyncScheduleTests
{
    [Fact]
    public void DelayUntilNextLocalHour_before_4am_same_day()
    {
        // 2026-08-05 02:00 UTC = 03:00 London (BST) → next 04:00 London is same day → 1h
        var now = new DateTimeOffset(2026, 8, 5, 2, 0, 0, TimeSpan.Zero);
        var delay = TraktSyncService.DelayUntilNextLocalHour(4, "Europe/London", now);
        Assert.True(delay > TimeSpan.FromMinutes(50) && delay < TimeSpan.FromMinutes(70), $"delay={delay}");
    }

    [Fact]
    public void DelayUntilNextLocalHour_after_4am_next_day()
    {
        // 2026-08-05 10:00 UTC = 11:00 London → next 04:00 is tomorrow
        var now = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        var delay = TraktSyncService.DelayUntilNextLocalHour(4, "Europe/London", now);
        Assert.True(delay > TimeSpan.FromHours(12) && delay < TimeSpan.FromHours(24), $"delay={delay}");
    }
}
