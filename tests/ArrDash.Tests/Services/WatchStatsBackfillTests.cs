using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class WatchStatsBackfillTests
{
    [Theory]
    [InlineData(0, 90, 90)]
    [InlineData(30, 90, 30)]
    [InlineData(1, 90, 1)]
    [InlineData(999, 90, 730)]
    public void ResolveBackfillDays_uses_default_when_pref_is_zero(int pref, int defaultDays, int expected) =>
        Assert.Equal(expected, WatchStatsBackfillHelper.Resolve(pref, defaultDays));

    [Theory]
    [InlineData(0, 365, 365)]
    [InlineData(30, 365, 30)]
    [InlineData(1, 365, 30)]
    [InlineData(9999, 365, 1825)]
    public void ResolveRetentionDays_uses_default_when_pref_is_zero(int pref, int defaultDays, int expected) =>
        Assert.Equal(expected, WatchStatsBackfillHelper.ResolveRetention(pref, defaultDays));
}
