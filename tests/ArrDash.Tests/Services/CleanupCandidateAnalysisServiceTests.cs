using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class CleanupCandidateAnalysisServiceTests
{
    [Fact]
    public void NormalizeSeriesTitle_strips_disambiguating_year_suffix()
    {
        Assert.Equal("BATTLESTAR GALACTICA", CleanupCandidateAnalysisService.NormalizeSeriesTitle("Battlestar Galactica (2003)"));
        Assert.Equal("THE OFFICE", CleanupCandidateAnalysisService.NormalizeSeriesTitle("The Office"));
    }

    [Fact]
    public void ResolveTagLabels_maps_known_ids_and_skips_unknown()
    {
        var labels = new Dictionary<(string Source, int TagId), string>
        {
            [("radarr", 1)] = "stevoid4-gmail-com",
            [("radarr", 2)] = "katrindrafnar",
            [("sonarr", 7)] = "katrindrafnar"
        };

        var resolved = CleanupCandidateAnalysisService.ResolveTagLabels("[-1,1,2]", "radarr", labels);

        Assert.Equal(["katrindrafnar", "stevoid4-gmail-com"], resolved);
    }

    [Fact]
    public void ResolveTagLabels_empty_or_invalid_returns_empty()
    {
        var labels = new Dictionary<(string Source, int TagId), string> { [("radarr", 1)] = "x" };

        Assert.Empty(CleanupCandidateAnalysisService.ResolveTagLabels("[]", "radarr", labels));
        Assert.Empty(CleanupCandidateAnalysisService.ResolveTagLabels("not-json", "radarr", labels));
        Assert.Empty(CleanupCandidateAnalysisService.ResolveTagLabels("[1]", "sonarr", labels));
    }

    [Fact]
    public void Build_includes_watchers_and_tags_on_candidates()
    {
        var added = DateTimeOffset.UtcNow.AddMonths(-18);
        var inventory = new List<MediaInventoryItemEntity>
        {
            new()
            {
                Source = "radarr",
                SourceItemId = 10,
                MediaType = "movie",
                Title = "Old Film",
                Year = 1999,
                TmdbId = 42,
                SizeOnDiskBytes = 5_000_000_000,
                HasFile = true,
                AddedUtc = added,
                TagsJson = "[1]",
                TitleSlug = "old-film"
            },
            new()
            {
                Source = "sonarr",
                SourceItemId = 20,
                MediaType = "series",
                Title = "Quiet Show (2001)",
                Year = 2001,
                SizeOnDiskBytes = 100_000_000,
                HasFile = true,
                AddedUtc = added,
                TagsJson = "[]",
                TitleSlug = "quiet-show",
                SeriesStatus = "ended"
            }
        };

        var movieLastPlayed = new Dictionary<int, DateTimeOffset>
        {
            [42] = DateTimeOffset.UtcNow.AddMonths(-14)
        };
        var seriesLastPlayed = new Dictionary<string, DateTimeOffset>();
        var movieWatchers = new Dictionary<int, IReadOnlyList<string>>
        {
            [42] = ["Mom", "Squiggley"]
        };
        var seriesWatchers = new Dictionary<string, IReadOnlyList<string>>();
        var tagLabels = new Dictionary<(string Source, int TagId), string>
        {
            [("radarr", 1)] = "stevoid4-gmail-com"
        };

        var results = CleanupCandidateAnalysisService.Build(
            inventory,
            movieLastPlayed,
            seriesLastPlayed,
            movieWatchers,
            seriesWatchers,
            tagLabels,
            thresholdMonths: 12,
            radarrBaseUrl: "https://radarr.example.com",
            sonarrBaseUrl: "https://sonarr.example.com");

        Assert.Equal(2, results.Count);

        var movie = Assert.Single(results, r => r.MediaType == "movie");
        Assert.Equal(["Mom", "Squiggley"], movie.WatchedBy);
        Assert.Equal(["stevoid4-gmail-com"], movie.Tags);
        Assert.Contains(CleanupReason.WatchedLongAgo, movie.Reasons);

        var series = Assert.Single(results, r => r.MediaType == "series");
        Assert.Empty(series.WatchedBy);
        Assert.Empty(series.Tags);
        Assert.Contains(CleanupReason.NeverWatched, series.Reasons);
        Assert.Equal("ended", series.SeriesStatus);
        Assert.False(movie.MarkedForDeletion);
    }

    [Fact]
    public void Build_keeps_marked_for_deletion_even_without_candidate_reasons()
    {
        // Ten larger files so this small marked item is not flagged "Largest" by the top-10% cut.
        var inventory = Enumerable.Range(1, 10)
            .Select(i => new MediaInventoryItemEntity
            {
                Source = "radarr",
                SourceItemId = i,
                MediaType = "movie",
                Title = $"Big {i}",
                TmdbId = 100 + i,
                SizeOnDiskBytes = 5_000_000_000L,
                HasFile = true,
                AddedUtc = DateTimeOffset.UtcNow.AddDays(-3),
                TagsJson = "[]",
                TitleSlug = $"big-{i}"
            })
            .Append(new MediaInventoryItemEntity
            {
                Source = "radarr",
                SourceItemId = 99,
                MediaType = "movie",
                Title = "Fresh Watch",
                TmdbId = 7,
                SizeOnDiskBytes = 50_000_000,
                HasFile = true,
                AddedUtc = DateTimeOffset.UtcNow.AddDays(-3),
                MarkedForDeletion = true,
                TagsJson = "[]",
                TitleSlug = "fresh-watch"
            })
            .ToList();

        var movieLastPlayed = new Dictionary<int, DateTimeOffset>
        {
            [7] = DateTimeOffset.UtcNow.AddHours(-2)
        };
        foreach (var i in Enumerable.Range(1, 10))
            movieLastPlayed[100 + i] = DateTimeOffset.UtcNow.AddHours(-1);

        var results = CleanupCandidateAnalysisService.Build(
            inventory,
            movieLastPlayed,
            new Dictionary<string, DateTimeOffset>(),
            new Dictionary<int, IReadOnlyList<string>>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<(string Source, int TagId), string>(),
            thresholdMonths: 12,
            radarrBaseUrl: null,
            sonarrBaseUrl: null);

        var item = Assert.Single(results, r => r.SourceItemId == 99);
        Assert.True(item.MarkedForDeletion);
        Assert.Empty(item.Reasons);
    }
}
