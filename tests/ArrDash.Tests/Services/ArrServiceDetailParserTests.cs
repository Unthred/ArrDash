using System.Text.Json;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class ArrServiceDetailParserTests
{
    [Fact]
    public void BuildArrDetail_NeedsAttention_WhenImportBlockedPresent()
    {
        var queue = Parse("""
            {
              "records": [
                { "trackedDownloadState": "importBlocked" },
                { "trackedDownloadState": "importBlocked" }
              ]
            }
            """);

        var detail = ArrServiceDetailParser.BuildArrDetail(
            "chaptarr",
            "Chaptarr",
            "https://chaptarr.example.com",
            "1.0",
            online: true,
            connectionError: null,
            health: null,
            commands: null,
            queueStatus: Parse("""{ "totalCount": 2 }"""),
            queue,
            history: null,
            workload: null);

        Assert.Equal(ServiceAttentionLevel.NeedsAttention, detail.Attention);
        Assert.Contains(detail.Problems, p => p.Message.Contains("blocked", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, detail.QueueStates.First(s => s.State == "importBlocked").Count);
    }

    [Fact]
    public void BuildArrDetail_Busy_WhenWorkloadPresentWithoutProblems()
    {
        var workload = new ServiceWorkload(ServiceWorkloadKind.Searching, "Book search");

        var detail = ArrServiceDetailParser.BuildArrDetail(
            "chaptarr",
            "Chaptarr",
            null,
            "1.0",
            true,
            null,
            null,
            null,
            null,
            null,
            null,
            workload);

        Assert.Equal(ServiceAttentionLevel.Busy, detail.Attention);
    }

    [Fact]
    public void BuildArrDetail_Offline_WhenNotOnline()
    {
        var detail = ArrServiceDetailParser.BuildArrDetail(
            "chaptarr",
            "Chaptarr",
            null,
            null,
            false,
            "500 Internal Server Error",
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(ServiceAttentionLevel.Offline, detail.Attention);
        Assert.Equal("500 Internal Server Error", detail.ConnectionError);
    }

    [Fact]
    public void ParseHealthProblems_ReadsWarningAndError()
    {
        var health = Parse("""
            [
              { "type": "warning", "message": "Indexer unavailable", "wikiUrl": "https://wiki.example.com" },
              { "type": "error", "message": "Disk full" }
            ]
            """);

        var problems = ArrServiceDetailParser.ParseHealthProblems(health);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Severity == "warning");
        Assert.Contains(problems, p => p.Severity == "error");
    }

    [Fact]
    public void ParseCommands_PrioritizesStarted()
    {
        var commands = Parse("""
            [
              { "name": "RssSync", "status": "completed", "startedOn": "2026-07-04T10:00:00Z" },
              { "name": "BookSearch", "status": "started", "startedOn": "2026-07-04T12:00:00Z" }
            ]
            """);

        var rows = ArrServiceDetailParser.ParseCommands(commands);

        Assert.Equal("BookSearch", rows[0].Name);
        Assert.Equal("started", rows[0].Status);
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
