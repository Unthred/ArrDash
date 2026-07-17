using ArrDash.Models;

namespace ArrDash.Tests.Services;

public sealed class WatchStatsPeriodHelperTests
{
    [Theory]
    [InlineData(WatchStatsPeriod.Today, 1)]
    [InlineData(WatchStatsPeriod.Days7, 7)]
    [InlineData(WatchStatsPeriod.Days30, 30)]
    [InlineData(WatchStatsPeriod.Year, 365)]
    [InlineData(WatchStatsPeriod.All, WatchStatsPeriodHelper.AllLookbackDays)]
    public void ToDays_maps_periods(WatchStatsPeriod period, int expected) =>
        Assert.Equal(expected, WatchStatsPeriodHelper.ToDays(period));

    [Theory]
    [InlineData("week", WatchStatsPeriod.Days7)]
    [InlineData("7d", WatchStatsPeriod.Days7)]
    [InlineData("month", WatchStatsPeriod.Days30)]
    [InlineData("30d", WatchStatsPeriod.Days30)]
    [InlineData("1y", WatchStatsPeriod.Year)]
    [InlineData("all", WatchStatsPeriod.All)]
    [InlineData("custom", WatchStatsPeriod.Custom)]
    public void Parse_maps_legacy_labels(string value, WatchStatsPeriod expected) =>
        Assert.Equal(expected, WatchStatsPeriodHelper.Parse(value));

    [Fact]
    public void ParseRange_reads_custom_dates()
    {
        var range = WatchStatsPeriodHelper.ParseRange("custom", "2025-01-01", "2025-06-01");
        Assert.Equal(WatchStatsPeriod.Custom, range.Period);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), range.CustomStartUtc);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), range.CustomEndUtc);
        Assert.Equal(153, WatchStatsPeriodHelper.ToDays(range));
    }

    [Fact]
    public void PanelCatalog_includes_watch_stats() =>
        Assert.Contains(PanelCatalog.All, p => p.Id == "watch-stats");
}
