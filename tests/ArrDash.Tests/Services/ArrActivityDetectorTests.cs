using System.Text.Json;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class ArrActivityDetectorTests
{
    [Fact]
    public void Detect_ReturnsSearch_WhenEpisodeSearchStarted()
    {
        var commands = Parse("""
            [
              { "name": "EpisodeSearch", "status": "started" }
            ]
            """);

        var workload = ArrActivityDetector.Detect(commands, null, null);

        Assert.NotNull(workload);
        Assert.Equal(ServiceWorkloadKind.Searching, workload!.Kind);
        Assert.Contains("Episode search", workload.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_PrefersStartedOverQueuedCommands()
    {
        var commands = Parse("""
            [
              { "name": "RssSync", "status": "queued" },
              { "name": "EpisodeSearch", "status": "started" }
            ]
            """);

        var workload = ArrActivityDetector.Detect(commands, null, null);

        Assert.NotNull(workload);
        Assert.Equal(ServiceWorkloadKind.Searching, workload!.Kind);
    }

    [Fact]
    public void Detect_ReturnsImporting_WhenQueueHasActivelyImportingItems()
    {
        var queue = Parse("""
            {
              "records": [
                { "status": "completed", "trackedDownloadState": "importBlocked" },
                { "status": "completed", "trackedDownloadState": "importing" }
              ]
            }
            """);

        var workload = ArrActivityDetector.Detect(null, null, queue);

        Assert.NotNull(workload);
        Assert.Equal(ServiceWorkloadKind.Importing, workload!.Kind);
        Assert.Equal(1, workload.Count);
    }

    [Fact]
    public void Detect_ReturnsNull_WhenQueueOnlyImportBlocked()
    {
        var queue = Parse("""
            {
              "records": [
                { "status": "completed", "trackedDownloadState": "importBlocked" },
                { "status": "completed", "trackedDownloadState": "importPending" }
              ]
            }
            """);

        var workload = ArrActivityDetector.Detect(null, null, queue);

        Assert.Null(workload);
    }

    [Fact]
    public void Detect_ReturnsDownloading_WhenQueueHasActiveDownloads()
    {
        var queueStatus = Parse("""{ "totalCount": 1 }""");
        var queue = Parse("""
            [
              { "status": "downloading", "trackedDownloadState": "downloading" }
            ]
            """);

        var workload = ArrActivityDetector.Detect(null, queueStatus, queue);

        Assert.NotNull(workload);
        Assert.Equal(ServiceWorkloadKind.Downloading, workload!.Kind);
    }

    [Fact]
    public void Detect_PrioritizesImportOverDownload()
    {
        var commands = Parse("""
            [
              { "name": "RssSync", "status": "started" }
            ]
            """);
        var queue = Parse("""
            [
              { "status": "completed", "trackedDownloadState": "importing" }
            ]
            """);

        var workload = ArrActivityDetector.Detect(commands, null, queue);

        Assert.NotNull(workload);
        Assert.Equal(ServiceWorkloadKind.Importing, workload!.Kind);
        Assert.Contains("Importing", workload.Label);
        Assert.Contains("RSS sync", workload.Label);
    }

    [Fact]
    public void Detect_IgnoresQueuedCommands()
    {
        var commands = Parse("""
            [
              { "name": "RssSync", "status": "queued" }
            ]
            """);

        Assert.Null(ArrActivityDetector.Detect(commands, null, null));
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
