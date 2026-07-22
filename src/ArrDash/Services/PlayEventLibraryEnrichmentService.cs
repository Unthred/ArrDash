using ArrDash.Configuration;
using ArrDash.Data;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArrDash.Services;

/// <summary>
/// Backfills library + provider ids on play events imported without them
/// (Tracearr rows carry none; older Tautulli rows lack section_id). Runs after each history
/// sync, capped per run, so the warehouse converges without hammering the servers (#38).
/// Also backfills season/episode from Tracearr history (imported rows were skip-if-exists).
/// Items the servers no longer know are remembered in-process and not retried until restart.
/// </summary>
public sealed class PlayEventLibraryEnrichmentService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TautulliClient tautulli,
    TracearrClient tracearr,
    EmbyPlaybackReportingClient emby,
    JellyfinPlaybackReportingClient jellyfin,
    LayoutPreferencesService prefs,
    IOptions<WatchStatsOptions> watchStatsOptions,
    ILogger<PlayEventLibraryEnrichmentService> logger)
{
    private const int PlexLookupsPerRun = 150;
    private const int MediaServerItemsPerRun = 400;
    private const int MediaServerBatchSize = 50;
    private const int ProviderIdItemsPerRun = 400;
    private const int SeriesEpisodeLookupsPerRun = 40;

    private readonly WatchStatsOptions _options = watchStatsOptions.Value;
    private readonly HashSet<string> _unresolvable = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unresolvableProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unresolvableSeriesEpisodes = new(StringComparer.OrdinalIgnoreCase);

    public async Task EnrichAsync(CancellationToken ct)
    {
        try
        {
            await EnrichMediaServerAsync(WatchStatsSources.Emby, emby, ct);
            await EnrichMediaServerAsync(WatchStatsSources.Jellyfin, jellyfin, ct);
            await EnrichPlexAsync(ct);
            await EnrichProviderIdsAsync(WatchStatsSources.Emby, emby, ct);
            await EnrichProviderIdsAsync(WatchStatsSources.Jellyfin, jellyfin, ct);
            // Tracearr already has S/E for nearly all Emby episodes; patch warehouse rows that
            // were imported before those fields existed / before we upserted metadata.
            await EnrichEpisodeIndexesFromTracearrAsync(WatchStatsSources.Emby, ct);
            await EnrichEpisodeIndexesFromTracearrAsync(WatchStatsSources.Jellyfin, ct);
            await EnrichEpisodeIndexesFromSeriesAsync(WatchStatsSources.Emby, emby, ct);
            await EnrichEpisodeIndexesFromSeriesAsync(WatchStatsSources.Jellyfin, jellyfin, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Library enrichment failed");
        }
    }

    private async Task EnrichMediaServerAsync(string source, PlaybackReportingClient client, CancellationToken ct)
    {
        if (!client.IsConfigured)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var itemIds = await PendingItemIdsAsync(db, source, ct);
        itemIds = itemIds.Where(id => !_unresolvable.Contains($"{source}:{id}")).Take(MediaServerItemsPerRun).ToList();
        if (itemIds.Count == 0)
            return;

        var roots = await client.FetchLibraryRootsAsync(ct);
        if (roots.Count == 0 || roots.All(r => r.Locations.Count == 0))
            return;

        var updated = 0;
        foreach (var batch in itemIds.Chunk(MediaServerBatchSize))
        {
            var paths = await client.FetchItemPathsAsync(batch, ct);
            foreach (var itemId in batch)
            {
                if (!paths.TryGetValue(itemId, out var path))
                {
                    _unresolvable.Add($"{source}:{itemId}");
                    continue;
                }

                // Longest matching location wins so nested library roots resolve correctly.
                var root = roots
                    .SelectMany(r => r.Locations.Select(l => (Root: r, Location: l)))
                    .Where(x => path.StartsWith(x.Location.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(path, x.Location, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Location.Length)
                    .Select(x => x.Root)
                    .FirstOrDefault();

                if (root is null)
                {
                    _unresolvable.Add($"{source}:{itemId}");
                    continue;
                }

                updated += await db.PlayEvents
                    .Where(e => e.Source == source
                                && e.ExternalItemId == itemId
                                && (e.LibraryExternalId == null || e.LibraryExternalId == ""))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.LibraryExternalId, root.Id)
                        .SetProperty(e => e.LibraryName, root.Name), ct);
            }
        }

        if (updated > 0)
            logger.LogInformation("Library enrichment: tagged {Count} {Source} play events", updated, source);
    }

    private async Task EnrichProviderIdsAsync(string source, PlaybackReportingClient client, CancellationToken ct)
    {
        if (!client.IsConfigured)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var itemIds = await db.PlayEvents.AsNoTracking()
            .Where(e => e.Source == source
                        && e.ExternalItemId != null && e.ExternalItemId != ""
                        && (e.ImdbId == null || e.ImdbId == "")
                        && e.TmdbId == null
                        && e.TvdbId == null
                        && e.TraktId == null)
            .Select(e => e.ExternalItemId!)
            .Distinct()
            .ToListAsync(ct);

        itemIds = itemIds
            .Where(id => !_unresolvableProviders.Contains($"{source}:{id}"))
            .Take(ProviderIdItemsPerRun)
            .ToList();
        if (itemIds.Count == 0)
            return;

        var updated = 0;
        foreach (var batch in itemIds.Chunk(MediaServerBatchSize))
        {
            var info = await client.FetchItemProviderInfoAsync(batch, ct);
            foreach (var itemId in batch)
            {
                if (!info.TryGetValue(itemId, out var meta)
                    || (string.IsNullOrWhiteSpace(meta.ImdbId)
                        && meta.TmdbId is null
                        && meta.TvdbId is null
                        && meta.TraktId is null))
                {
                    _unresolvableProviders.Add($"{source}:{itemId}");
                    continue;
                }

                var imdb = meta.ImdbId;
                var tmdb = meta.TmdbId;
                var tvdb = meta.TvdbId;
                var trakt = meta.TraktId;
                var year = meta.Year;
                var isEpisodeItem = string.Equals(meta.Type, "Episode", StringComparison.OrdinalIgnoreCase);

                // Tracearr often stores the *series* id on episode plays. Never stamp that
                // item's IndexNumber (null) or a single CanonicalMediaKey across all plays.
                if (!isEpisodeItem)
                {
                    updated += await db.PlayEvents
                        .Where(e => e.Source == source && e.ExternalItemId == itemId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(e => e.ImdbId, e => e.ImdbId == null || e.ImdbId == "" ? imdb : e.ImdbId)
                            .SetProperty(e => e.TmdbId, e => e.TmdbId == null ? tmdb : e.TmdbId)
                            .SetProperty(e => e.TvdbId, e => e.TvdbId == null ? tvdb : e.TvdbId)
                            .SetProperty(e => e.TraktId, e => e.TraktId == null ? trakt : e.TraktId)
                            .SetProperty(e => e.Year, e => e.Year == null ? year : e.Year), ct);

                    await RebuildCanonicalKeysAsync(db, source, itemId, ct);
                    continue;
                }

                var season = meta.SeasonNumber;
                var episode = meta.EpisodeNumber;
                var sample = await db.PlayEvents.AsNoTracking()
                    .Where(e => e.Source == source && e.ExternalItemId == itemId)
                    .Select(e => new { e.MediaType, e.Title, e.SeriesTitle, e.SeasonNumber, e.EpisodeNumber, e.Year })
                    .FirstOrDefaultAsync(ct);

                var mediaType = sample?.MediaType ?? "episode";
                var title = string.Equals(mediaType, "episode", StringComparison.OrdinalIgnoreCase)
                    ? sample?.SeriesTitle ?? sample?.Title
                    : sample?.Title;
                var key = CanonicalMediaKeyBuilder.Build(
                    mediaType,
                    imdb,
                    tmdb,
                    tvdb,
                    trakt,
                    title,
                    year ?? sample?.Year,
                    season ?? sample?.SeasonNumber,
                    episode ?? sample?.EpisodeNumber);

                updated += await db.PlayEvents
                    .Where(e => e.Source == source && e.ExternalItemId == itemId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.ImdbId, e => e.ImdbId == null || e.ImdbId == "" ? imdb : e.ImdbId)
                        .SetProperty(e => e.TmdbId, e => e.TmdbId == null ? tmdb : e.TmdbId)
                        .SetProperty(e => e.TvdbId, e => e.TvdbId == null ? tvdb : e.TvdbId)
                        .SetProperty(e => e.TraktId, e => e.TraktId == null ? trakt : e.TraktId)
                        .SetProperty(e => e.Year, e => e.Year == null ? year : e.Year)
                        .SetProperty(e => e.SeasonNumber, e => e.SeasonNumber == null ? season : e.SeasonNumber)
                        .SetProperty(e => e.EpisodeNumber, e => e.EpisodeNumber == null ? episode : e.EpisodeNumber)
                        .SetProperty(e => e.CanonicalMediaKey, key), ct);
            }
        }

        if (updated > 0)
            logger.LogInformation("Provider-id enrichment: tagged {Count} {Source} play events", updated, source);
    }

    private async Task EnrichEpisodeIndexesFromTracearrAsync(string sourceKey, CancellationToken ct)
    {
        if (!tracearr.IsConfigured)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var needsPatch = await db.PlayEvents.AsNoTracking()
            .AnyAsync(e => e.Source == sourceKey
                           && e.MediaType == "episode"
                           && (e.SeasonNumber == null || e.EpisodeNumber == null)
                           && e.ExternalPlayId.StartsWith("tracearr:"), ct);
        if (!needsPatch)
            return;

        var backfillDays = WatchStatsBackfillHelper.Resolve(prefs.Current.WatchStatsBackfillDays, _options.BackfillDays);
        // Prefer at least a year so Trakt backfill can cover the UI "year" window.
        backfillDays = Math.Max(backfillDays, 370);
        var since = DateTimeOffset.UtcNow.AddDays(-backfillDays);
        var maxRows = Math.Min(backfillDays * 200, 50_000);

        var history = await tracearr.FetchHistoryForMetadataBackfillAsync(sourceKey, since, maxRows, ct);
        if (history.Count == 0)
            return;

        var byPlayId = history
            .Where(h => h.SeasonNumber is not null && h.EpisodeNumber is not null)
            .GroupBy(h => h.ExternalPlayId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = await db.PlayEvents
            .Where(e => e.Source == sourceKey
                        && e.MediaType == "episode"
                        && (e.SeasonNumber == null || e.EpisodeNumber == null)
                        && e.ExternalPlayId.StartsWith("tracearr:"))
            .ToListAsync(ct);

        var patched = 0;
        foreach (var row in rows)
        {
            if (!byPlayId.TryGetValue(row.ExternalPlayId, out var incoming))
                continue;
            if (PlayEventImportHelper.TryBackfillEpisodeMetadata(row, incoming))
                patched++;
        }

        if (patched > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Tracearr episode-index enrichment: patched {Count} {Source} play events", patched, sourceKey);
        }
    }

    private async Task EnrichEpisodeIndexesFromSeriesAsync(
        string source,
        PlaybackReportingClient client,
        CancellationToken ct)
    {
        if (!client.IsConfigured)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var seriesIds = await db.PlayEvents.AsNoTracking()
            .Where(e => e.Source == source
                        && e.MediaType == "episode"
                        && e.ExternalItemId != null && e.ExternalItemId != ""
                        && e.ItemTitle != null && e.ItemTitle != ""
                        && (e.SeasonNumber == null || e.EpisodeNumber == null))
            .Select(e => e.ExternalItemId!)
            .Distinct()
            .ToListAsync(ct);

        seriesIds = seriesIds
            .Where(id => !_unresolvableSeriesEpisodes.Contains($"{source}:{id}"))
            .Take(SeriesEpisodeLookupsPerRun)
            .ToList();
        if (seriesIds.Count == 0)
            return;

        var patched = 0;
        foreach (var seriesId in seriesIds)
        {
            var episodes = await client.FetchSeriesEpisodesAsync(seriesId, ct);
            if (episodes.Count == 0)
            {
                _unresolvableSeriesEpisodes.Add($"{source}:{seriesId}");
                continue;
            }

            var byName = episodes
                .Where(e => !string.IsNullOrWhiteSpace(e.Name) && e.SeasonNumber is not null && e.EpisodeNumber is not null)
                .GroupBy(e => e.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = await db.PlayEvents
                .Where(e => e.Source == source
                            && e.ExternalItemId == seriesId
                            && e.MediaType == "episode"
                            && e.ItemTitle != null && e.ItemTitle != ""
                            && (e.SeasonNumber == null || e.EpisodeNumber == null))
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                if (!byName.TryGetValue(row.ItemTitle!.Trim(), out var match))
                    continue;

                row.SeasonNumber ??= match.SeasonNumber;
                row.EpisodeNumber ??= match.EpisodeNumber;
                row.CanonicalMediaKey = CanonicalMediaKeyBuilder.Build(
                    row.MediaType,
                    row.ImdbId,
                    row.TmdbId,
                    row.TvdbId,
                    row.TraktId,
                    row.SeriesTitle ?? row.Title,
                    row.Year,
                    row.SeasonNumber,
                    row.EpisodeNumber);
                patched++;
            }

            await db.SaveChangesAsync(ct);
        }

        if (patched > 0)
            logger.LogInformation("Series-episode enrichment: patched {Count} {Source} play events", patched, source);
    }

    private static async Task RebuildCanonicalKeysAsync(
        ArrDashDbContext db,
        string source,
        string itemId,
        CancellationToken ct)
    {
        var rows = await db.PlayEvents
            .Where(e => e.Source == source && e.ExternalItemId == itemId)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.CanonicalMediaKey = CanonicalMediaKeyBuilder.Build(
                row.MediaType,
                row.ImdbId,
                row.TmdbId,
                row.TvdbId,
                row.TraktId,
                row.MediaType == "episode" ? row.SeriesTitle ?? row.Title : row.Title,
                row.Year,
                row.SeasonNumber,
                row.EpisodeNumber);
        }

        if (rows.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task EnrichPlexAsync(CancellationToken ct)
    {
        if (!tautulli.IsConfigured)
            return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ratingKeys = await PendingItemIdsAsync(db, WatchStatsSources.Plex, ct);
        ratingKeys = ratingKeys.Where(k => !_unresolvable.Contains($"plex:{k}")).Take(PlexLookupsPerRun).ToList();
        if (ratingKeys.Count == 0)
            return;

        var updated = 0;
        foreach (var ratingKey in ratingKeys)
        {
            var (sectionId, libraryName) = await tautulli.GetLibraryForRatingKeyAsync(ratingKey, ct);
            if (string.IsNullOrWhiteSpace(sectionId))
            {
                _unresolvable.Add($"plex:{ratingKey}");
                continue;
            }

            updated += await db.PlayEvents
                .Where(e => e.Source == WatchStatsSources.Plex
                            && e.ExternalItemId == ratingKey
                            && (e.LibraryExternalId == null || e.LibraryExternalId == ""))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.LibraryExternalId, sectionId)
                    .SetProperty(e => e.LibraryName, libraryName), ct);
        }

        if (updated > 0)
            logger.LogInformation("Library enrichment: tagged {Count} plex play events", updated);
    }

    private static async Task<List<string>> PendingItemIdsAsync(ArrDashDbContext db, string source, CancellationToken ct) =>
        await db.PlayEvents.AsNoTracking()
            .Where(e => e.Source == source
                        && (e.LibraryExternalId == null || e.LibraryExternalId == "")
                        && e.ExternalItemId != null && e.ExternalItemId != "")
            .Select(e => e.ExternalItemId!)
            .Distinct()
            .ToListAsync(ct);
}
