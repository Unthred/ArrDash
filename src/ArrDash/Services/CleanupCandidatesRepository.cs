using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public sealed class CleanupCandidatesRepository(IDbContextFactory<ArrDashDbContext> dbFactory)
{
    public async Task<IReadOnlyList<MediaInventoryItemEntity>> GetInventoryAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.MediaInventoryItems.AsNoTracking().ToListAsync(ct);
    }

    public async Task SetNeverDeleteAsync(string source, int sourceItemId, bool neverDelete, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (neverDelete)
        {
            await db.MediaInventoryItems
                .Where(e => e.Source == source && e.SourceItemId == sourceItemId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.NeverDelete, true)
                    .SetProperty(e => e.MarkedForDeletion, false), ct);
        }
        else
        {
            await db.MediaInventoryItems
                .Where(e => e.Source == source && e.SourceItemId == sourceItemId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NeverDelete, false), ct);
        }
    }

    public async Task SetMarkedForDeletionAsync(string source, int sourceItemId, bool markedForDeletion, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (markedForDeletion)
        {
            await db.MediaInventoryItems
                .Where(e => e.Source == source && e.SourceItemId == sourceItemId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.MarkedForDeletion, true)
                    .SetProperty(e => e.NeverDelete, false), ct);
        }
        else
        {
            await db.MediaInventoryItems
                .Where(e => e.Source == source && e.SourceItemId == sourceItemId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.MarkedForDeletion, false), ct);
        }
    }

    public async Task<IReadOnlyDictionary<(string Source, int TagId), string>> GetTagLabelsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ArrTags.AsNoTracking()
            .Select(t => new { t.Source, t.TagId, t.Label })
            .ToListAsync(ct);

        return rows.ToDictionary(
            t => (t.Source, t.TagId),
            t => t.Label,
            EqualityComparer<(string Source, int TagId)>.Default);
    }

    public async Task<IReadOnlyDictionary<int, DateTimeOffset>> GetMovieLastPlayedAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PlayEvents
            .Where(e => e.MediaType == "movie" && e.TmdbId != null)
            .GroupBy(e => e.TmdbId!.Value)
            .Select(g => new { TmdbId = g.Key, LastPlayedUtc = g.Max(e => e.PlayedAtUtc) })
            .ToDictionaryAsync(x => x.TmdbId, x => new DateTimeOffset(x.LastPlayedUtc, TimeSpan.Zero), ct);
    }

    // Keyed by normalized series title, not TvdbId: TVDB assigns a distinct ID to every episode
    // as well as to the show itself, and PlayEventEntity.TvdbId (populated from Trakt/Plex/Emby
    // episode metadata) is the per-episode ID — it does not and cannot match Sonarr's per-show
    // TvdbId in MediaInventoryItems. Title is the only field both sides populate at show level.
    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetSeriesLastPlayedAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // First pass grouped in SQL on the raw title (cheap, cuts ~27k rows down to a few hundred);
        // second pass in-memory applies the full normalization (strips Sonarr's disambiguating
        // " (YYYY)" suffix, which SQLite/EF can't translate as a regex) and merges any titles that
        // collapse onto the same normalized key, keeping the most recent play of the two.
        var rawGroups = await db.PlayEvents
            .Where(e => e.MediaType == "episode" && e.SeriesTitle != null)
            .GroupBy(e => e.SeriesTitle!.Trim().ToUpper())
            .Select(g => new { SeriesTitle = g.Key, LastPlayedUtc = g.Max(e => e.PlayedAtUtc) })
            .ToListAsync(ct);

        var result = new Dictionary<string, DateTimeOffset>();
        foreach (var group in rawGroups)
        {
            var key = CleanupCandidateAnalysisService.NormalizeSeriesTitle(group.SeriesTitle);
            var played = new DateTimeOffset(group.LastPlayedUtc, TimeSpan.Zero);
            if (!result.TryGetValue(key, out var existing) || played > existing)
                result[key] = played;
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetMovieWatchersAsync(
        IReadOnlyList<WatchStatsUserAlias> aliases,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.PlayEvents.AsNoTracking()
            .Where(e => e.MediaType == "movie" && e.TmdbId != null && e.UserDisplayName != "")
            .Select(e => new { TmdbId = e.TmdbId!.Value, e.Source, e.UserDisplayName })
            .Distinct()
            .ToListAsync(ct);

        return AggregateWatchers(
            rows.Select(r => (Key: r.TmdbId, r.Source, r.UserDisplayName)),
            aliases);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetSeriesWatchersAsync(
        IReadOnlyList<WatchStatsUserAlias> aliases,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.PlayEvents.AsNoTracking()
            .Where(e => e.MediaType == "episode" && e.SeriesTitle != null && e.UserDisplayName != "")
            .Select(e => new { SeriesTitle = e.SeriesTitle!, e.Source, e.UserDisplayName })
            .Distinct()
            .ToListAsync(ct);

        return AggregateWatchers(
            rows.Select(r => (
                Key: CleanupCandidateAnalysisService.NormalizeSeriesTitle(r.SeriesTitle),
                r.Source,
                r.UserDisplayName)),
            aliases);
    }

    private static IReadOnlyDictionary<TKey, IReadOnlyList<string>> AggregateWatchers<TKey>(
        IEnumerable<(TKey Key, string Source, string UserDisplayName)> rows,
        IReadOnlyList<WatchStatsUserAlias> aliases)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, HashSet<string>>();
        foreach (var row in rows)
        {
            var name = ResolveCanonicalUser(row.Source, row.UserDisplayName, aliases);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!result.TryGetValue(row.Key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[row.Key] = set;
            }

            set.Add(name);
        }

        return result.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string ResolveCanonicalUser(string source, string user, IReadOnlyList<WatchStatsUserAlias> aliases)
    {
        var alias = aliases.FirstOrDefault(a =>
            string.Equals(a.Source, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.SourceUserName, user, StringComparison.OrdinalIgnoreCase));

        return alias?.CanonicalName ?? user;
    }
}
