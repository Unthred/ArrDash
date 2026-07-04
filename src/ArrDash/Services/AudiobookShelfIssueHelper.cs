using ArrDash.Models;

namespace ArrDash.Services;

public static class AudiobookShelfIssueHelper
{
    public static ServiceProblem IssueSummaryProblem(int issueCount) =>
        new(
            "warning",
            issueCount == 1
                ? "1 library item has issues — metadata or file problems detected"
                : $"{issueCount} library items have issues — metadata or file problems detected");

    public static ServiceHealth ApplyIssueAttention(ServiceHealth health, int issueCount)
    {
        if (issueCount <= 0)
            return health;

        return health with
        {
            Attention = ServiceAttentionLevel.NeedsAttention,
            AttentionLabel = ArrServiceDetailParser.AttentionLabelFor(ServiceAttentionLevel.NeedsAttention),
            AttentionCount = issueCount
        };
    }
}
