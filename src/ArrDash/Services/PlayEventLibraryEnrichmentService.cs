using ArrDash.Data;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

/// <summary>
/// Backfills LibraryExternalId/LibraryName on play events imported without library info
/// (Tracearr rows carry none; older Tautulli rows lack section_id). Runs after each history
/// sync, capped per run, so the warehouse converges without hammering the servers (#38).
/// Items the servers no longer know are remembered in-process and not retried until restart.
/// </summary>
public sealed class PlayEventLibraryEnrichmentService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TautulliClient tautulli,
    EmbyPlaybackReportingClient emby,
    JellyfinPlaybackReportingClient jellyfin,
    ILogger<PlayEventLibraryEnrichmentService> logger)
{
    private const int PlexLookupsPerRun = 150;
    private const int MediaServerItemsPerRun = 400;
    private const int MediaServerBatchSize = 50;

    private readonly HashSet<string> _unresolvable = new(StringComparer.OrdinalIgnoreCase);

    public async Task EnrichAsync(CancellationToken ct)
    {
        try
        {
            await EnrichMediaServerAsync(WatchStatsSources.Emby, emby, ct);
            await EnrichMediaServerAsync(WatchStatsSources.Jellyfin, jellyfin, ct);
            await EnrichPlexAsync(ct);
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
