using System.Text.Json;
using System.Text.RegularExpressions;
using ArrDash.Data.Entities;
using ArrDash.Models;

namespace ArrDash.Services;

public static class CleanupCandidateAnalysisService
{
    private static readonly Regex TrailingYearPattern = new(@"\s*\(\d{4}\)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Sonarr appends " (YYYY)" to a series title only when TheTVDB has another show with the
    /// identical name (e.g. "Battlestar Galactica (2003)" vs the 1978 original) — Plex/Emby/Trakt
    /// watch history never carries that suffix, so it must be stripped before either side is used
    /// as a join key or every disambiguated show would wrongly show as never watched.
    /// </summary>
    public static string NormalizeSeriesTitle(string title) =>
        TrailingYearPattern.Replace(title, "").Trim().ToUpperInvariant();

    public static IReadOnlyList<string> ResolveTagLabels(
        string tagsJson,
        string source,
        IReadOnlyDictionary<(string Source, int TagId), string> tagLabels)
    {
        if (string.IsNullOrWhiteSpace(tagsJson) || tagsJson == "[]")
            return [];

        List<int>? ids;
        try
        {
            ids = JsonSerializer.Deserialize<List<int>>(tagsJson);
        }
        catch (JsonException)
        {
            return [];
        }

        if (ids is null || ids.Count == 0)
            return [];

        return ids
            .Distinct()
            .Where(id => tagLabels.ContainsKey((source, id)))
            .Select(id => tagLabels[(source, id)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<CleanupCandidateItem> Build(
        IReadOnlyList<MediaInventoryItemEntity> inventory,
        IReadOnlyDictionary<int, DateTimeOffset> movieLastPlayed,
        IReadOnlyDictionary<string, DateTimeOffset> seriesLastPlayed,
        IReadOnlyDictionary<int, IReadOnlyList<string>> movieWatchers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> seriesWatchers,
        IReadOnlyDictionary<(string Source, int TagId), string> tagLabels,
        int thresholdMonths,
        string? radarrBaseUrl,
        string? sonarrBaseUrl)
    {
        if (inventory.Count == 0)
            return [];

        var cutoff = DateTimeOffset.UtcNow.AddMonths(-Math.Max(thresholdMonths, 1));
        var largestThresholdBytes = ComputeLargestThreshold(inventory);

        var results = new List<CleanupCandidateItem>();
        foreach (var item in inventory)
        {
            if (!item.HasFile || item.SizeOnDiskBytes <= 0)
                continue;

            DateTimeOffset? lastWatched = item.MediaType switch
            {
                "movie" when item.TmdbId is int tmdbId && movieLastPlayed.TryGetValue(tmdbId, out var m) => m,
                "series" when seriesLastPlayed.TryGetValue(NormalizeSeriesTitle(item.Title), out var s) => s,
                _ => null
            };

            IReadOnlyList<string> watchedBy = item.MediaType switch
            {
                "movie" when item.TmdbId is int tmdbId && movieWatchers.TryGetValue(tmdbId, out var mw) => mw,
                "series" when seriesWatchers.TryGetValue(NormalizeSeriesTitle(item.Title), out var sw) => sw,
                _ => []
            };

            var tags = ResolveTagLabels(item.TagsJson, item.Source, tagLabels);

            var reasons = new List<CleanupReason>();

            if (lastWatched is null && item.AddedUtc is DateTimeOffset added && added <= cutoff)
                reasons.Add(CleanupReason.NeverWatched);
            else if (lastWatched is DateTimeOffset watched && watched <= cutoff)
                reasons.Add(CleanupReason.WatchedLongAgo);

            if (item.SizeOnDiskBytes >= largestThresholdBytes)
                reasons.Add(CleanupReason.Largest);

            // Keep marked-for-deletion items on the list even when they no longer match
            // candidate reasons, so the deletion queue does not silently drop them.
            if (reasons.Count == 0 && !item.MarkedForDeletion)
                continue;

            var source = item.MediaType == "movie" ? MediaSource.Radarr : MediaSource.Sonarr;
            var baseUrl = item.MediaType == "movie" ? radarrBaseUrl : sonarrBaseUrl;

            results.Add(new CleanupCandidateItem(
                item.Source,
                item.SourceItemId,
                item.MediaType,
                item.Title,
                item.Year,
                item.SizeOnDiskBytes,
                item.AddedUtc,
                lastWatched,
                watchedBy,
                tags,
                reasons,
                ServiceDeepLinkBuilder.BuildItemUrl(source, baseUrl, item.TitleSlug),
                item.ImdbId,
                item.NeverDelete,
                item.MarkedForDeletion,
                item.Rating,
                item.SeriesStatus));
        }

        return results
            .OrderByDescending(r => r.SizeBytes)
            .ToList();
    }

    /// <summary>Top-10%-by-size threshold, so "Largest" stays a meaningful cut regardless of library size.</summary>
    private static long ComputeLargestThreshold(IReadOnlyList<MediaInventoryItemEntity> inventory)
    {
        var sizes = inventory
            .Where(i => i.HasFile && i.SizeOnDiskBytes > 0)
            .Select(i => i.SizeOnDiskBytes)
            .OrderDescending()
            .ToList();

        if (sizes.Count == 0)
            return long.MaxValue;

        var topCount = Math.Max(1, sizes.Count / 10);
        return sizes[topCount - 1];
    }
}
