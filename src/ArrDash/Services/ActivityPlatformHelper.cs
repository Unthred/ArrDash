using ArrDash.Models;

namespace ArrDash.Services;

public static class ActivityPlatformHelper
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";

        return name.Trim() switch
        {
            "Microsoft Edge" => "Edge",
            "Plex Web" => "Web",
            _ => name.Trim()
        };
    }

    public static IReadOnlyList<ActivityChartPoint> MergePoints(IEnumerable<ActivityChartPoint> points) =>
        points
            .GroupBy(p => p.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ActivityChartPoint(g.Key, g.Sum(p => p.Value)))
            .OrderByDescending(p => p.Value)
            .ToList();
}
