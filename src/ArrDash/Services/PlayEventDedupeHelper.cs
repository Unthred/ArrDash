using ArrDash.Data.Entities;
using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Collapses Combined Activity/Watch Stats so one user + one title within 24 hours
/// counts as a single watch (cross-source and stop/start).
/// </summary>
public static class PlayEventDedupeHelper
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    public static IReadOnlyList<PlayEventEntity> CollapseForCombined(
        IReadOnlyList<PlayEventEntity> events,
        TimeSpan? window = null)
    {
        if (events.Count <= 1)
            return events;

        var windowSpan = window ?? DefaultWindow;
        var survivors = new List<PlayEventEntity>();

        foreach (var group in events.GroupBy(MediaGroupKey, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(e => e.PlayedAtUtc).ToList();
            var cluster = new List<PlayEventEntity> { ordered[0] };

            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = cluster[^1];
                var next = ordered[i];
                if (next.PlayedAtUtc - prev.PlayedAtUtc <= windowSpan)
                {
                    cluster.Add(next);
                    continue;
                }

                survivors.Add(PickSurvivor(cluster));
                cluster = [next];
            }

            survivors.Add(PickSurvivor(cluster));
        }

        return survivors
            .OrderByDescending(e => e.PlayedAtUtc)
            .ToList();
    }

    private static string MediaGroupKey(PlayEventEntity e)
    {
        var user = e.UserDisplayName?.Trim() ?? "";
        var media = !string.IsNullOrWhiteSpace(e.CanonicalMediaKey)
            ? e.CanonicalMediaKey.Trim()
            : FallbackMediaKey(e);
        return $"{user}\u001f{media}";
    }

    private static string FallbackMediaKey(PlayEventEntity e)
    {
        var type = (e.MediaType ?? "other").Trim().ToLowerInvariant();
        var title = (e.SeriesTitle ?? e.Title ?? "unknown").Trim().ToLowerInvariant();
        var year = e.Year ?? 0;
        var season = e.SeasonNumber ?? 0;
        var episode = e.EpisodeNumber ?? 0;
        return $"{type}|{title}|{year}|s{season}e{episode}";
    }

    private static PlayEventEntity PickSurvivor(List<PlayEventEntity> cluster)
    {
        if (cluster.Count == 1)
            return cluster[0];

        var earliest = cluster.Min(e => e.PlayedAtUtc);
        var best = cluster
            .OrderBy(e => e.DurationIsEstimated)
            .ThenByDescending(e => e.DurationSeconds)
            .ThenByDescending(RichnessScore)
            .ThenBy(SourceRank)
            .ThenBy(e => e.PlayedAtUtc)
            .First();

        if (best.PlayedAtUtc == earliest)
            return best;

        return CloneWithPlayedAt(best, earliest);
    }

    private static int RichnessScore(PlayEventEntity e)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(e.LibraryName) || !string.IsNullOrWhiteSpace(e.LibraryExternalId))
            score += 4;
        if (!string.IsNullOrWhiteSpace(e.Client))
            score += 2;
        if (!string.IsNullOrWhiteSpace(e.Platform))
            score += 2;
        if (!string.IsNullOrWhiteSpace(e.ThumbPath))
            score += 2;
        if (!string.IsNullOrWhiteSpace(e.TranscodeDecision))
            score += 1;
        if (!string.IsNullOrWhiteSpace(e.ExternalItemId))
            score += 1;
        return score;
    }

    private static int SourceRank(PlayEventEntity e) => e.Source?.ToLowerInvariant() switch
    {
        WatchStatsSources.Plex => 0,
        WatchStatsSources.Emby => 1,
        WatchStatsSources.Jellyfin => 2,
        WatchStatsSources.Trakt => 3,
        _ => 4
    };

    private static PlayEventEntity CloneWithPlayedAt(PlayEventEntity source, DateTime playedAtUtc) => new()
    {
        Id = source.Id,
        Source = source.Source,
        ExternalPlayId = source.ExternalPlayId,
        UserDisplayName = source.UserDisplayName,
        UserExternalId = source.UserExternalId,
        Title = source.Title,
        SeriesTitle = source.SeriesTitle,
        MediaType = source.MediaType,
        GenresJson = source.GenresJson,
        Client = source.Client,
        Platform = source.Platform,
        PlayedAtUtc = playedAtUtc,
        DurationSeconds = source.DurationSeconds,
        ExternalItemId = source.ExternalItemId,
        ItemTitle = source.ItemTitle,
        GrandparentExternalId = source.GrandparentExternalId,
        ThumbPath = source.ThumbPath,
        TranscodeDecision = source.TranscodeDecision,
        LibraryName = source.LibraryName,
        LibraryExternalId = source.LibraryExternalId,
        ProgressPercent = source.ProgressPercent,
        Origin = source.Origin,
        Year = source.Year,
        SeasonNumber = source.SeasonNumber,
        EpisodeNumber = source.EpisodeNumber,
        ImdbId = source.ImdbId,
        TmdbId = source.TmdbId,
        TvdbId = source.TvdbId,
        TraktId = source.TraktId,
        WasCompleted = source.WasCompleted,
        DurationIsEstimated = source.DurationIsEstimated,
        CanonicalMediaKey = source.CanonicalMediaKey
    };
}
