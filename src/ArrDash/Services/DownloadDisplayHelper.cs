namespace ArrDash.Services;

using ArrDash.Models;

public static class DownloadDisplayHelper
{
    public sealed record EpisodeAvailability(int EpisodeNumber, bool HasFile, DateTimeOffset? AirDateUtc = null);

    public static string FormatEpisodeLine(int? seasonNumber, IReadOnlyList<int>? episodeNumbers)
    {
        if (episodeNumbers is null or { Count: 0 })
            return string.Empty;

        var episodes = FormatEpisodeRange(episodeNumbers);
        return seasonNumber is > 0 ? $"S{seasonNumber:00} · {episodes}" : episodes;
    }

    public static string FormatSeasonLabel(int seasonNumber) =>
        seasonNumber > 0 ? $"Season {seasonNumber}" : string.Empty;

    public static IReadOnlyList<int> BuildBadgeEpisodeNumbers(
        IReadOnlyList<int> presentEpisodes,
        IReadOnlyList<EpisodeAvailability>? seasonEpisodes)
    {
        if (seasonEpisodes is { Count: > 0 })
        {
            var min = seasonEpisodes.Min(e => e.EpisodeNumber);
            var max = seasonEpisodes.Max(e => e.EpisodeNumber);
            return Enumerable.Range(min, max - min + 1).ToList();
        }

        if (presentEpisodes.Count == 0)
            return [];

        return ExpandEpisodeBadgeNumbers(presentEpisodes, includeMissing: presentEpisodes.Count >= 2);
    }

    public static IReadOnlyList<int> BuildOnDiskEpisodeNumbers(IReadOnlyList<EpisodeAvailability>? seasonEpisodes) =>
        seasonEpisodes is null
            ? []
            : seasonEpisodes.Where(e => e.HasFile).Select(e => e.EpisodeNumber).OrderBy(n => n).ToList();

    public static IReadOnlyList<int> BuildUnairedEpisodeNumbers(
        IReadOnlyList<EpisodeAvailability>? seasonEpisodes,
        DateTimeOffset? asOf = null)
    {
        asOf ??= DateTimeOffset.UtcNow;
        if (seasonEpisodes is null)
            return [];

        return seasonEpisodes
            .Where(e => !e.HasFile && e.AirDateUtc is { } air && air > asOf)
            .Select(e => e.EpisodeNumber)
            .OrderBy(n => n)
            .ToList();
    }

    public static bool IsUnairedEpisode(int episodeNumber, IReadOnlyList<int>? unairedEpisodeNumbers) =>
        unairedEpisodeNumbers is not null && unairedEpisodeNumbers.Contains(episodeNumber);

    public static IReadOnlyDictionary<int, DateTimeOffset> BuildEpisodeAirDates(
        IReadOnlyList<EpisodeAvailability>? seasonEpisodes) =>
        seasonEpisodes is null
            ? new Dictionary<int, DateTimeOffset>()
            : seasonEpisodes
                .Where(e => e.AirDateUtc is not null)
                .ToDictionary(e => e.EpisodeNumber, e => e.AirDateUtc!.Value);

    public static string FormatEpisodeAirDate(DateTimeOffset airUtc)
    {
        var local = airUtc.ToLocalTime();
        var now = DateTimeOffset.Now;
        return local.Year == now.Year
            ? $"Airs {local:ddd d MMM}"
            : $"Airs {local:ddd d MMM yyyy}";
    }

    public static string? GetEpisodeAirLabel(
        int episodeNumber,
        IReadOnlyDictionary<int, DateTimeOffset>? episodeAirDates)
    {
        if (episodeAirDates is null || !episodeAirDates.TryGetValue(episodeNumber, out var air))
            return null;

        return FormatEpisodeAirDate(air);
    }

    public static bool IsMissingEpisodeNumber(int episodeNumber, IReadOnlyList<int> episodeNumbers) =>
        !episodeNumbers.Contains(episodeNumber);

    public static bool IsMissingOnDisk(
        int episodeNumber,
        IReadOnlyList<int>? onDiskEpisodeNumbers,
        IReadOnlyList<int>? presentEpisodes)
    {
        if (onDiskEpisodeNumbers is { Count: > 0 })
            return !onDiskEpisodeNumbers.Contains(episodeNumber);

        if (presentEpisodes is { Count: > 0 })
            return IsMissingEpisodeNumber(episodeNumber, presentEpisodes);

        return false;
    }

    public static IReadOnlyList<int> ResolveBadgeEpisodeNumbers(DownloadItem item)
    {
        if (item.BadgeEpisodeNumbers is { Count: > 0 })
            return item.BadgeEpisodeNumbers;

        if (item.EpisodeNumbers is not { Count: > 0 })
            return [];

        return ExpandEpisodeBadgeNumbers(item.EpisodeNumbers, includeMissing: item.EpisodeNumbers.Count >= 2);
    }

    public static IReadOnlyList<int> ExpandEpisodeBadgeNumbers(
        IReadOnlyList<int> episodeNumbers,
        bool includeMissing)
    {
        if (episodeNumbers.Count == 0)
            return [];

        var sorted = episodeNumbers.OrderBy(n => n).ToList();
        if (!includeMissing || sorted.Count < 2)
            return sorted;

        return Enumerable.Range(sorted[0], sorted[^1] - sorted[0] + 1).ToList();
    }

    public static string FormatEpisodeRange(IReadOnlyList<int> episodeNumbers)
    {
        if (episodeNumbers.Count == 0)
            return string.Empty;

        if (episodeNumbers.Count == 1)
            return $"E{episodeNumbers[0]:00}";

        var sorted = episodeNumbers.OrderBy(n => n).ToList();
        var segments = new List<string>();
        var start = sorted[0];
        var previous = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == previous + 1)
            {
                previous = sorted[i];
                continue;
            }

            segments.Add(start == previous ? $"E{start:00}" : $"E{start:00}–E{previous:00}");
            start = sorted[i];
            previous = sorted[i];
        }

        segments.Add(start == previous ? $"E{start:00}" : $"E{start:00}–E{previous:00}");
        return string.Join(", ", segments);
    }
}
