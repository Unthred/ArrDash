using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class HistoryTitleFormatterTests
{
    [Theory]
    [InlineData(
        "The.Walking.Dead.Dead.City.2023.S02E08.1080p.BluRay.x265",
        "The Walking Dead Dead City - S02E08")]
    [InlineData("Heel.2025.1080p.WEBRip.x265-DH", "Heel (2025)")]
    [InlineData(
        "/tv/The Walking Dead - Dead City/Season 2/The Walking Dead - Dead City - S02E08 - If History Were a Consequence.mkv",
        "The Walking Dead - Dead City - S02E08")]
    [InlineData(
        "Joe Abercrombie - Red Country - unabridged audiobook",
        "Joe Abercrombie - Red Country - unabridged audiobook")]
    public void FormatSourceTitle_ProducesFriendlyNames(string sourceTitle, string expected)
    {
        Assert.Equal(expected, HistoryTitleFormatter.FormatSourceTitle(sourceTitle));
    }

    [Fact]
    public void DetermineAttentionFromSnapshot_NeedsAttention_WhenImportBlocked()
    {
        using var queueDoc = System.Text.Json.JsonDocument.Parse("""
            {
              "records": [
                { "trackedDownloadState": "importBlocked" }
              ]
            }
            """);

        var attention = ArrServiceDetailParser.DetermineAttentionFromSnapshot(
            online: true,
            health: null,
            queueStatus: null,
            queue: queueDoc.RootElement.Clone(),
            workload: null);

        Assert.Equal(Models.ServiceAttentionLevel.NeedsAttention, attention);
    }
}
