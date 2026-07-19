using System.Text.Json;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public sealed class WatchStatsRepository(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    LayoutPreferencesService prefs)
{
    public async Task<long> CountEventsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PlayEvents.LongCountAsync(ct);
    }

    public async Task<IReadOnlyList<PlayEventEntity>> QueryEventsAsync(
        WatchStatsRange range,
        string? source = null,
        CancellationToken ct = default)
    {
        var (startUtc, endUtc) = range.Bounds();
        var endExclusive = endUtc.Date.AddDays(1);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.PlayEvents.AsNoTracking()
            .Where(e => e.PlayedAtUtc >= startUtc && e.PlayedAtUtc < endExclusive);

        if (!string.IsNullOrWhiteSpace(source))
        {
            if (source.Contains(',', StringComparison.Ordinal))
            {
                var keys = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                query = query.Where(e => keys.Contains(e.Source));
            }
            else
            {
                query = query.Where(e => e.Source == source);
            }
        }

        var events = await query.ToListAsync(ct);
        return ApplyLibraryFilter(events);
    }

    public async Task<IReadOnlyList<PlayEventEntity>> QueryEventsAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        IReadOnlyList<string> sources,
        CancellationToken ct = default)
    {
        var (startUtc, endUtc) = range.Bounds();
        var endExclusive = endUtc.Date.AddDays(1);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.PlayEvents.AsNoTracking()
            .Where(e => e.PlayedAtUtc >= startUtc && e.PlayedAtUtc < endExclusive);

        query = ApplySourceFilter(query, filter, sources);

        var events = await query.ToListAsync(ct);
        return ApplyLibraryFilter(events);
    }

    private static IQueryable<PlayEventEntity> ApplySourceFilter(
        IQueryable<PlayEventEntity> query,
        WatchStatsSourceFilter filter,
        IReadOnlyList<string> sources)
    {
        var keys = WatchStatsSourceFilters.ToSourceKeys(filter);
        if (keys.Count == 0 || filter == WatchStatsSourceFilter.Combined)
            return query.Where(e => sources.Contains(e.Source));

        if (keys.Count == 1)
        {
            var only = keys[0];
            return query.Where(e => e.Source == only);
        }

        return query.Where(e => keys.Contains(e.Source));
    }

    private IReadOnlyList<PlayEventEntity> ApplyLibraryFilter(IReadOnlyList<PlayEventEntity> events) =>
        WatchStatsLibraryFilter.Apply(
            events,
            prefs.Current.WatchStatsExcludedLibraries,
            e => e.Source,
            e => e.LibraryExternalId);

    public async Task<WatchStatsSourceSnapshot> BuildSourceSnapshotAsync(
        string source,
        WatchStatsRange range,
        int limit,
        CancellationToken ct = default)
    {
        var events = await QueryEventsAsync(range, source, ct);
        if (events.Count == 0)
        {
            return EmptySnapshot(source, WatchStatsSources.Label(source));
        }

        var bundle = PlayEventAnalyticsService.Build(events, range, limit);
        return bundle.Leaderboard with
        {
            Key = source,
            Label = WatchStatsSources.Label(source)
        };
    }

    public async Task<WatchStatsSourceSnapshot> BuildCombinedSnapshotAsync(
        IReadOnlyList<string> sources,
        WatchStatsRange range,
        IReadOnlyList<WatchStatsUserAlias> aliases,
        int limit,
        CancellationToken ct = default)
    {
        var events = await QueryEventsAsync(range, WatchStatsSourceFilter.Combined, sources, ct);
        if (events.Count == 0)
        {
            return EmptySnapshot("combined", "Combined");
        }

        var mapped = PrepareCombinedEvents(events, aliases);
        var bundle = PlayEventAnalyticsService.Build(mapped, range, limit);
        return bundle.Leaderboard with { Key = "combined", Label = "Combined" };
    }

    public async Task<ActivityAnalyticsBundle> BuildAnalyticsAsync(
        WatchStatsRange range,
        WatchStatsSourceFilter filter,
        IReadOnlyList<string> sources,
        IReadOnlyList<WatchStatsUserAlias> aliases,
        int limit,
        CancellationToken ct = default)
    {
        var events = await QueryEventsAsync(range, filter, sources, ct);
        if (events.Count == 0)
            return ActivityAnalyticsBundle.Empty;

        if (WatchStatsSourceFilters.ShouldCollapse(filter))
            events = PrepareCombinedEvents(events, aliases);

        return PlayEventAnalyticsService.Build(events, range, limit);
    }

    /// <summary>
    /// Alias-remap users then collapse same user + media within 24h for Combined views.
    /// </summary>
    public static IReadOnlyList<PlayEventEntity> PrepareCombinedEvents(
        IReadOnlyList<PlayEventEntity> events,
        IReadOnlyList<WatchStatsUserAlias> aliases)
    {
        var mapped = events
            .Select(e => CloneWithUser(e, ResolveCanonicalUser(e.Source, e.UserDisplayName, aliases)))
            .ToList();
        return PlayEventDedupeHelper.CollapseForCombined(mapped);
    }

    private static WatchStatsSourceSnapshot EmptySnapshot(string key, string label) =>
        new(
            key,
            label,
            true,
            null,
            new WatchStatsSummary(0, 0, 0, null),
            [], [], [], [], [], [], [],
            [], [], []);

    private static DateTimeOffset? ToOffset(DateTime utc) => new(utc, TimeSpan.Zero);

    private static string ResolveCanonicalUser(string source, string user, IReadOnlyList<WatchStatsUserAlias> aliases)
    {
        var alias = aliases.FirstOrDefault(a =>
            string.Equals(a.Source, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.SourceUserName, user, StringComparison.OrdinalIgnoreCase));

        return alias?.CanonicalName ?? user;
    }

    private static PlayEventEntity CloneWithUser(PlayEventEntity source, string userDisplayName) => new()
    {
        Id = source.Id,
        Source = source.Source,
        ExternalPlayId = source.ExternalPlayId,
        UserDisplayName = userDisplayName,
        UserExternalId = source.UserExternalId,
        Title = source.Title,
        SeriesTitle = source.SeriesTitle,
        MediaType = source.MediaType,
        GenresJson = source.GenresJson,
        Client = source.Client,
        Platform = source.Platform,
        PlayedAtUtc = source.PlayedAtUtc,
        DurationSeconds = source.DurationSeconds,
        ExternalItemId = source.ExternalItemId,
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
        CanonicalMediaKey = source.CanonicalMediaKey,
        ItemTitle = source.ItemTitle
    };
}
