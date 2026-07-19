using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class PlayEventDedupeHelperTests
{
    private static readonly DateTime Base = new(2026, 7, 18, 20, 0, 0, DateTimeKind.Utc);
    private const string MovieKey = "movie:imdb:tt0111161";

    private static PlayEventEntity Play(
        string source,
        string user,
        DateTime playedAtUtc,
        string? canonicalKey = MovieKey,
        string title = "The Shawshank Redemption",
        int durationSeconds = 7200,
        bool durationEstimated = false,
        string? library = "Movies",
        string? client = "Chrome",
        string? platform = "Web",
        string? thumb = "/thumb.jpg") => new()
    {
        Source = source,
        ExternalPlayId = $"{source}:{user}:{playedAtUtc:O}",
        UserDisplayName = user,
        Title = title,
        MediaType = "movie",
        PlayedAtUtc = playedAtUtc,
        DurationSeconds = durationSeconds,
        DurationIsEstimated = durationEstimated,
        CanonicalMediaKey = canonicalKey,
        LibraryName = library,
        Client = client,
        Platform = platform,
        ThumbPath = thumb,
        WasCompleted = true,
        Year = 1994
    };

    [Fact]
    public void EmbyAndTrakt_SameMovie_Within24h_CollapseToOne()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Emby, "Squiggley", Base, durationSeconds: 7000),
            Play(WatchStatsSources.Trakt, "Squiggley", Base.AddHours(1), durationSeconds: 8520, durationEstimated: true,
                library: null, client: null, platform: null, thumb: null),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        var survivor = Assert.Single(collapsed);
        Assert.Equal(WatchStatsSources.Emby, survivor.Source);
        Assert.Equal(Base, survivor.PlayedAtUtc);
        Assert.Equal(7000, survivor.DurationSeconds);
        Assert.False(survivor.DurationIsEstimated);
    }

    [Fact]
    public void PlexEmbyTrakt_SameMovie_CollapseToOne()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Plex, "Squiggley", Base.AddMinutes(5), durationSeconds: 7100),
            Play(WatchStatsSources.Emby, "Squiggley", Base, durationSeconds: 6900),
            Play(WatchStatsSources.Trakt, "Squiggley", Base.AddHours(2), durationEstimated: true,
                library: null, client: null, platform: null, thumb: null),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        Assert.Single(collapsed);
        Assert.Equal(WatchStatsSources.Plex, collapsed[0].Source);
        Assert.Equal(Base, collapsed[0].PlayedAtUtc);
    }

    [Fact]
    public void SameTitle_25HoursApart_RemainTwo()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Emby, "Squiggley", Base),
            Play(WatchStatsSources.Trakt, "Squiggley", Base.AddHours(25), durationEstimated: true),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        Assert.Equal(2, collapsed.Count);
    }

    [Fact]
    public void StopStart_SameSource_ThreeTimes_CollapseToOne()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Emby, "Squiggley", Base, durationSeconds: 600),
            Play(WatchStatsSources.Emby, "Squiggley", Base.AddHours(1), durationSeconds: 1200),
            Play(WatchStatsSources.Emby, "Squiggley", Base.AddHours(2), durationSeconds: 7200),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        var survivor = Assert.Single(collapsed);
        Assert.Equal(7200, survivor.DurationSeconds);
        Assert.Equal(Base, survivor.PlayedAtUtc);
    }

    [Fact]
    public void DifferentUsers_SameTitle_RemainTwo()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Emby, "Alice", Base),
            Play(WatchStatsSources.Plex, "Bob", Base.AddMinutes(10)),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        Assert.Equal(2, collapsed.Count);
    }

    [Fact]
    public void AliasRemapping_MergesAcrossSources()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Plex, "Margaret", Base, durationSeconds: 7000),
            Play(WatchStatsSources.Emby, "Mom", Base.AddHours(1), durationSeconds: 6800),
        };

        var aliases = new[]
        {
            new WatchStatsUserAlias("Mom", WatchStatsSources.Plex, "Margaret")
        };

        var collapsed = WatchStatsRepository.PrepareCombinedEvents(events, aliases);

        var survivor = Assert.Single(collapsed);
        Assert.Equal("Mom", survivor.UserDisplayName);
        Assert.Equal(WatchStatsSources.Plex, survivor.Source);
    }

    [Fact]
    public void FallbackKey_WithoutCanonical_StillCollapses()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Emby, "Squiggley", Base, canonicalKey: null, title: "Inception"),
            Play(WatchStatsSources.Trakt, "Squiggley", Base.AddHours(3), canonicalKey: "", title: "Inception",
                durationEstimated: true, library: null, client: null, platform: null, thumb: null),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        Assert.Single(collapsed);
    }

    [Fact]
    public void PrefersNonEstimatedDurationOverLongerEstimated()
    {
        var events = new[]
        {
            Play(WatchStatsSources.Trakt, "Squiggley", Base, durationSeconds: 9000, durationEstimated: true,
                library: null, client: null, platform: null, thumb: null),
            Play(WatchStatsSources.Emby, "Squiggley", Base.AddMinutes(30), durationSeconds: 7000, durationEstimated: false),
        };

        var collapsed = PlayEventDedupeHelper.CollapseForCombined(events);

        var survivor = Assert.Single(collapsed);
        Assert.Equal(WatchStatsSources.Emby, survivor.Source);
        Assert.Equal(7000, survivor.DurationSeconds);
        Assert.False(survivor.DurationIsEstimated);
    }

    [Fact]
    public void WithoutCollapse_AnalyticsCountsRawPlays()
    {
        // Per-source paths feed PlayEventAnalyticsService without CollapseForCombined.
        var events = new[]
        {
            Play(WatchStatsSources.Plex, "Squiggley", Base),
            Play(WatchStatsSources.Plex, "Squiggley", Base.AddHours(1)),
            Play(WatchStatsSources.Plex, "Squiggley", Base.AddHours(2)),
        };

        var raw = PlayEventAnalyticsService.Build(events, WatchStatsRange.Year);
        Assert.Equal(3, raw.Summary.TotalPlays);

        var combined = PlayEventAnalyticsService.Build(
            PlayEventDedupeHelper.CollapseForCombined(events),
            WatchStatsRange.Year);
        Assert.Equal(1, combined.Summary.TotalPlays);
    }
}
