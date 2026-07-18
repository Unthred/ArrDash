using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class PlayEventAnalyticsServiceRecentTests
{
    private static PlayEventEntity Episode(
        string user,
        string externalItemId,
        DateTime playedAtUtc,
        string? itemTitle = null,
        string source = WatchStatsSources.Emby,
        string seriesTitle = "Rick and Morty") => new()
    {
        Source = source,
        ExternalPlayId = $"{source}:{user}:{playedAtUtc:O}",
        UserDisplayName = user,
        Title = seriesTitle,
        SeriesTitle = seriesTitle,
        MediaType = "episode",
        ExternalItemId = externalItemId,
        ItemTitle = itemTitle,
        PlayedAtUtc = playedAtUtc,
        DurationSeconds = 1200,
        WasCompleted = true
    };

    [Fact]
    public void DifferentUsers_SameShow_ProduceSeparateRows()
    {
        var events = new[]
        {
            Episode("TamToucan", "935102-1", new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc), "Pilot"),
            Episode("Squiggley", "935102-2", new DateTime(2026, 7, 18, 9, 0, 0, DateTimeKind.Utc), "Lawnmower Dog"),
        };

        var recent = PlayEventAnalyticsService.Build(events, WatchStatsRange.Year).Leaderboard.Recent;

        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, r => r.Subtitle!.Contains("TamToucan"));
        Assert.Contains(recent, r => r.Subtitle!.Contains("Squiggley"));
    }

    [Fact]
    public void SameUser_SameItem_RepeatedPlays_CollapseIntoOneRowWithCount()
    {
        var events = new[]
        {
            Episode("Squiggley", "1284555", new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc), itemTitle: null),
            Episode("Squiggley", "1284555", new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc), itemTitle: "The Yellow Wood"),
            Episode("Squiggley", "1284555", new DateTime(2026, 7, 16, 10, 0, 0, DateTimeKind.Utc), itemTitle: "The Yellow Wood"),
        };

        var recent = PlayEventAnalyticsService.Build(events, WatchStatsRange.Year).Leaderboard.Recent;

        // Same native item id (1284555) must collapse into one row even though one play
        // is missing its episode title (Tracearr populates it intermittently) — title
        // presence must not fracture an otherwise-identical item into separate rows.
        var row = Assert.Single(recent);
        Assert.Equal(3, row.Plays);
    }

    [Fact]
    public void SameUser_DifferentEpisodes_ProduceSeparateRows()
    {
        var events = new[]
        {
            Episode("Squiggley", "935102-1", new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc), "Pilot"),
            Episode("Squiggley", "935102-2", new DateTime(2026, 7, 18, 9, 0, 0, DateTimeKind.Utc), "Lawnmower Dog"),
        };

        var recent = PlayEventAnalyticsService.Build(events, WatchStatsRange.Year).Leaderboard.Recent;

        Assert.Equal(2, recent.Count);
    }
}
