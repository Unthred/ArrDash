using System.Text.Json;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class AudiobookShelfActivityTrackerTests
{
    [Fact]
    public void GetCurrentWorkload_ReturnsNull_WhenNoScans()
    {
        var tracker = new AudiobookShelfActivityTracker();

        Assert.Null(tracker.GetCurrentWorkload());
    }

    [Fact]
    public void HandleScanStart_AndComplete_UpdateWorkload()
    {
        var tracker = new AudiobookShelfActivityTracker();
        tracker.HandleScanStart(Parse("""
            {
              "id": "lib_abc",
              "type": "scan",
              "name": "Audiobooks"
            }
            """));

        var active = tracker.GetCurrentWorkload();

        Assert.NotNull(active);
        Assert.Equal(ServiceWorkloadKind.Scanning, active!.Kind);
        Assert.Equal("Scanning Audiobooks", active.Label);

        tracker.HandleScanComplete(Parse("""
            {
              "id": "lib_abc",
              "type": "scan",
              "name": "Audiobooks"
            }
            """));

        Assert.Null(tracker.GetCurrentWorkload());
    }

    [Fact]
    public void HandleInit_SeedsActiveScans()
    {
        var tracker = new AudiobookShelfActivityTracker();
        tracker.SetLibraryNames(new Dictionary<string, string> { ["lib_abc"] = "Audiobooks" });
        tracker.HandleInit(["lib_abc"]);

        var active = tracker.GetCurrentWorkload();

        Assert.NotNull(active);
        Assert.Equal(ServiceWorkloadKind.Scanning, active!.Kind);
        Assert.Equal("Scanning Audiobooks", active.Label);
    }

    [Fact]
    public void HandleScanStart_UsesMatchKind()
    {
        var tracker = new AudiobookShelfActivityTracker();
        tracker.HandleScanStart(Parse("""
            {
              "id": "lib_abc",
              "type": "match",
              "name": "Audiobooks"
            }
            """));

        var active = tracker.GetCurrentWorkload();

        Assert.NotNull(active);
        Assert.Equal(ServiceWorkloadKind.Matching, active!.Kind);
        Assert.Equal("Matching Audiobooks", active.Label);
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
