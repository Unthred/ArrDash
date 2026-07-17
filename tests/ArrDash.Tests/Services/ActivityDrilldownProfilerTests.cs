using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class ActivityDrilldownProfilerTests
{
    private static ActivityDrilldownRequest Req(
        ActivityDrilldownKind kind,
        string key,
        string? title = null,
        string? source = null) =>
        new(kind, key, title ?? key, source, WatchStatsPeriod.Year);

    private static ActivityDrilldownProfiler.Event Ev(
        string user,
        string title,
        string mediaType,
        int durationSeconds,
        string[]? genres = null,
        string? series = null,
        string? source = "plex",
        string? userId = null,
        DateTimeOffset? playedAt = null) =>
        new(
            user,
            userId,
            title,
            series,
            mediaType,
            genres ?? [],
            playedAt ?? DateTimeOffset.UtcNow.AddHours(-1),
            durationSeconds,
            source,
            null);

    [Fact]
    public void Media_profile_lists_who_watched()
    {
        var events = new[]
        {
            Ev("Alice", "Dune", "movie", 7200),
            Ev("Bob", "Dune", "movie", 3600),
            Ev("Alice", "Dune", "movie", 1800),
            Ev("Carol", "Other", "movie", 9000),
        };

        var snap = ActivityDrilldownProfiler.Build(Req(ActivityDrilldownKind.Media, "Dune"), events);

        Assert.Equal(3, snap.TotalPlays);
        Assert.Equal(2, snap.UniqueUsers);
        Assert.NotNull(snap.TopUsers);
        Assert.Equal(2, snap.TopUsers.Count);
        Assert.Equal("Alice", snap.TopUsers[0].Title);
        Assert.Equal(ActivityDrilldownKind.User, snap.TopUsers[0].DrilldownKind);
        Assert.False(string.IsNullOrWhiteSpace(snap.TopUsers[0].DrilldownKey));
    }

    [Fact]
    public void User_profile_builds_movies_tv_genres_and_mix()
    {
        var events = new[]
        {
            Ev("Alice", "Dune", "movie", 7200, ["Sci-Fi", "Adventure"]),
            Ev("Alice", "Foundation", "episode", 3600, ["Sci-Fi"], series: "Foundation"),
            Ev("Alice", "Song", "music", 600, ["Pop"]),
            Ev("Bob", "Dune", "movie", 7200, ["Sci-Fi"]),
        };

        var snap = ActivityDrilldownProfiler.Build(Req(ActivityDrilldownKind.User, "Alice"), events);

        Assert.Equal(3, snap.TotalPlays);
        Assert.NotNull(snap.TopMovies);
        Assert.Single(snap.TopMovies);
        Assert.Equal("Dune", snap.TopMovies[0].Title);
        Assert.Equal(ActivityDrilldownKind.Media, snap.TopMovies[0].DrilldownKind);

        Assert.NotNull(snap.TopTv);
        Assert.Single(snap.TopTv);
        Assert.Equal("Foundation", snap.TopTv[0].Title);

        Assert.NotNull(snap.TopGenres);
        Assert.Contains(snap.TopGenres, g => g.Title == "Sci-Fi" && g.DrilldownKind == ActivityDrilldownKind.Genre);

        Assert.NotNull(snap.MediaMix);
        Assert.Equal(2.0, snap.MediaMix.MovieHours);
        Assert.Equal(1.0, snap.MediaMix.TvHours);
        Assert.Equal(0.2, snap.MediaMix.MusicHours);
    }

    [Fact]
    public void Genre_profile_filters_and_lists_titles_and_users()
    {
        var events = new[]
        {
            Ev("Alice", "Dune", "movie", 7200, ["Sci-Fi"]),
            Ev("Bob", "Foundation", "episode", 3600, ["Sci-Fi"], series: "Foundation"),
            Ev("Carol", "Romance Film", "movie", 5400, ["Romance"]),
        };

        var snap = ActivityDrilldownProfiler.Build(Req(ActivityDrilldownKind.Genre, "Sci-Fi"), events);

        Assert.Equal(2, snap.TotalPlays);
        Assert.Equal(2, snap.UniqueUsers);
        Assert.NotNull(snap.TopUsers);
        Assert.Equal(2, snap.TopUsers.Count);
        Assert.NotNull(snap.TopMovies);
        Assert.Single(snap.TopMovies);
        Assert.Equal("Dune", snap.TopMovies[0].Title);
        Assert.NotNull(snap.TopTv);
        Assert.Single(snap.TopTv);
    }

    [Fact]
    public void Source_filter_limits_events()
    {
        var events = new[]
        {
            Ev("Alice", "Dune", "movie", 7200, source: "plex"),
            Ev("Alice", "Dune", "movie", 3600, source: "emby"),
        };

        var snap = ActivityDrilldownProfiler.Build(
            Req(ActivityDrilldownKind.Media, "Dune", source: "plex"),
            events);

        Assert.Equal(1, snap.TotalPlays);
        Assert.Equal(2.0, snap.TotalHours);
    }

    [Fact]
    public void Rows_without_drilldown_key_are_not_clickable_candidates()
    {
        var platform = new WatchStatRow("Roku", "12 plays", 3.5, 12, null, null, "plex");
        Assert.Null(platform.DrilldownKind);
        Assert.True(string.IsNullOrWhiteSpace(platform.DrilldownKey));
    }

    [Fact]
    public void Media_profile_matches_prefixed_rating_key_by_title()
    {
        var events = new[]
        {
            Ev("Alice", "Jurassic World: Rebirth", "movie", 7200),
            Ev("Bob", "Jurassic World: Rebirth", "movie", 3600),
            Ev("Carol", "Other Film", "movie", 9000),
        };

        var snap = ActivityDrilldownProfiler.Build(
            new ActivityDrilldownRequest(
                ActivityDrilldownKind.Media,
                "r:12345",
                "Jurassic World: Rebirth",
                "plex",
                WatchStatsPeriod.Year),
            events);

        Assert.Equal(2, snap.TotalPlays);
        Assert.Equal(2, snap.UniqueUsers);
        Assert.Equal(3.0, snap.TotalHours);
    }

    [Fact]
    public void Empty_match_returns_zero_totals()
    {
        var events = new[]
        {
            Ev("Alice", "Dune", "movie", 7200),
        };

        var snap = ActivityDrilldownProfiler.Build(Req(ActivityDrilldownKind.User, "Nobody"), events);

        Assert.Equal(0, snap.TotalPlays);
        Assert.Equal(0, snap.TotalHours);
        Assert.Empty(snap.Plays);
    }
}
