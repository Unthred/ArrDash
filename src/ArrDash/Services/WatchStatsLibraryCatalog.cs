using ArrDash.Data;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

/// <summary>
/// Discovers media-server libraries for the Activity include/exclude picker.
/// </summary>
public sealed class WatchStatsLibraryCatalog(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    PlexHistoryClient plexHistory,
    TautulliClient tautulli,
    EmbyPlaybackReportingClient emby,
    JellyfinPlaybackReportingClient jellyfin)
{
    public async Task<IReadOnlyList<WatchStatsLibraryInfo>> ListAsync(CancellationToken ct = default)
    {
        var byKey = new Dictionary<string, WatchStatsLibraryInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var lib in await FromDatabaseAsync(ct))
            byKey[lib.Key] = lib;

        foreach (var lib in await plexHistory.FetchLibrariesAsync(ct))
            Merge(byKey, lib);

        foreach (var lib in await tautulli.FetchLibrariesAsync(ct))
            Merge(byKey, lib);

        foreach (var lib in await emby.FetchLibrariesAsync(ct))
            Merge(byKey, lib);

        foreach (var lib in await jellyfin.FetchLibrariesAsync(ct))
            Merge(byKey, lib);

        return byKey.Values
            .OrderBy(l => l.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<WatchStatsLibraryInfo>> FromDatabaseAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.PlayEvents.AsNoTracking()
            .Where(e => e.LibraryExternalId != null && e.LibraryExternalId != "")
            .Select(e => new { e.Source, e.LibraryExternalId, e.LibraryName })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LibraryExternalId))
            .Select(r => new WatchStatsLibraryInfo(
                r.Source,
                r.LibraryExternalId!,
                string.IsNullOrWhiteSpace(r.LibraryName) ? $"Library {r.LibraryExternalId}" : r.LibraryName!))
            .ToList();
    }

    private static void Merge(Dictionary<string, WatchStatsLibraryInfo> byKey, WatchStatsLibraryInfo lib)
    {
        if (byKey.TryGetValue(lib.Key, out var existing))
        {
            var name = !string.IsNullOrWhiteSpace(lib.Name) && !lib.Name.StartsWith("Library ", StringComparison.Ordinal)
                ? lib.Name
                : existing.Name;
            byKey[lib.Key] = existing with
            {
                Name = name,
                MediaType = lib.MediaType ?? existing.MediaType
            };
        }
        else
        {
            byKey[lib.Key] = lib;
        }
    }
}
