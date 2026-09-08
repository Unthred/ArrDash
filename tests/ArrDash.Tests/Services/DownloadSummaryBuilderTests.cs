using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class DownloadSummaryBuilderTests
{
    [Fact]
    public void Build_counts_imports_and_grabs_inside_the_selected_window()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var summary = DownloadSummaryBuilder.Build(24,
            [Item(MediaSource.Sonarr, DownloadEvent.Imported, now.AddHours(-2)), Item(MediaSource.Sonarr, DownloadEvent.Grabbed, now.AddHours(-3)), Item(MediaSource.Sonarr, DownloadEvent.Imported, now.AddHours(-25))], [], [], [], [], now);
        var sonarr = Assert.Single(summary.Services, service => service.Source == MediaSource.Sonarr);
        Assert.Equal(1, sonarr.ImportedCount);
        Assert.Equal(1, sonarr.GrabbedCount);
    }

    [Fact]
    public void Build_keeps_audiobookshelf_separate_from_chaptarr()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var summary = DownloadSummaryBuilder.Build(24, [], [],
            [Item(MediaSource.Chaptarr, DownloadEvent.Imported, now.AddHours(-1))],
            [Item(MediaSource.AudiobookShelf, DownloadEvent.Imported, now.AddHours(-1))], [], now);

        Assert.Equal(1, Assert.Single(summary.Services, s => s.Source == MediaSource.Chaptarr).ImportedCount);
        Assert.Equal(1, Assert.Single(summary.Services, s => s.Source == MediaSource.AudiobookShelf).ImportedCount);
    }

    private static DownloadItem Item(MediaSource source, DownloadEvent @event, DateTimeOffset timestamp) => new(Guid.NewGuid().ToString("N"), source, MediaType.Tv, "Test", null, @event, timestamp, null, null, null);
}
