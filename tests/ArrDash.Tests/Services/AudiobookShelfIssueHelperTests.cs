using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Services;

public sealed class AudiobookShelfIssueHelperTests
{
    [Fact]
    public void ApplyIssueAttention_SetsNeedsAttentionAndCount()
    {
        var health = new ServiceHealth("audiobookshelf", "AudioBookShelf", true, true, null, null);

        var updated = AudiobookShelfIssueHelper.ApplyIssueAttention(health, 95);

        Assert.Equal(ServiceAttentionLevel.NeedsAttention, updated.Attention);
        Assert.Equal(95, updated.AttentionCount);
    }

    [Fact]
    public void IssueSummaryProblem_UsesSingularForOne()
    {
        var problem = AudiobookShelfIssueHelper.IssueSummaryProblem(1);

        Assert.Contains("1 library item has issues", problem.Message, StringComparison.OrdinalIgnoreCase);
    }
}
