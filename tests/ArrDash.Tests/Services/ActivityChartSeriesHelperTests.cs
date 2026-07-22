using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class ActivityChartSeriesHelperTests
{
    [Fact]
    public void BuildMediaMixPieRows_WarehouseHoursShape_UsesCategoriesAsSlices()
    {
        var mix = new ActivityMediaMix(
            ["Movies", "TV", "Music", "Other"],
            [new ActivityMediaSeries("Hours", [47.9, 24.0, 0, 0])]);

        var rows = ActivityChartSeriesHelper.BuildMediaMixPieRows(mix);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Movies", rows[0].Label);
        Assert.Equal(47.9, rows[0].Value);
        Assert.Equal("TV", rows[1].Label);
        Assert.Equal(24.0, rows[1].Value);
    }

    [Fact]
    public void BuildMediaMixPieRows_LegacyTautulliShape_ConvertsSecondsToHours()
    {
        var mix = new ActivityMediaMix(
            ["ignored-dates"],
            [
                new ActivityMediaSeries("Movies", [7200, 3600]),
                new ActivityMediaSeries("TV", [1800])
            ]);

        var rows = ActivityChartSeriesHelper.BuildMediaMixPieRows(mix);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Movies", rows[0].Label);
        Assert.Equal(3.0, rows[0].Value);
        Assert.Equal("TV", rows[1].Label);
        Assert.Equal(0.5, rows[1].Value);
    }

    [Fact]
    public void Build_PlaysOverTime_EmitsServerSeriesForStackedChart()
    {
        var day = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Play(WatchStatsSources.Plex, day),
            Play(WatchStatsSources.Plex, day.AddHours(1)),
            Play(WatchStatsSources.Emby, day),
            Play(WatchStatsSources.Trakt, day.AddDays(1)),
        };

        var bundle = PlayEventAnalyticsService.Build(events, new WatchStatsRange(WatchStatsPeriod.Days30));
        var points = bundle.PlaysOverTime;

        Assert.Contains(points, p => p.Series == "Plex" && p.Value == 2);
        Assert.Contains(points, p => p.Series == "Emby" && p.Value == 1);
        Assert.Contains(points, p => p.Series == "Trakt" && p.Value == 1);
        Assert.Contains(points, p => p.Series == "Total");
        Assert.DoesNotContain(points, p => p.Series is null);
    }

    private static PlayEventEntity Play(string source, DateTime playedAtUtc) => new()
    {
        Source = source,
        ExternalPlayId = $"{source}:{playedAtUtc:O}",
        UserDisplayName = "Squiggley",
        Title = "Test",
        MediaType = "movie",
        PlayedAtUtc = playedAtUtc,
        DurationSeconds = 3600,
        WasCompleted = true
    };
}
