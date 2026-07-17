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
            query = query.Where(e => e.Source == source);

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

        query = filter switch
        {
            WatchStatsSourceFilter.Plex => query.Where(e => e.Source == WatchStatsSources.Plex),
            WatchStatsSourceFilter.Emby => query.Where(e => e.Source == WatchStatsSources.Emby),
            WatchStatsSourceFilter.Jellyfin => query.Where(e => e.Source == WatchStatsSources.Jellyfin),
            _ => query.Where(e => sources.Contains(e.Source))
        };

        var events = await query.ToListAsync(ct);
        return ApplyLibraryFilter(events);
    }

    private IReadOnlyList<PlayEventEntity> ApplyLibraryFilter(IReadOnlyList<PlayEventEntity> events) =>
        WatchStatsLibraryFilter.Apply(
            events,
            prefs.Current.WatchStatsIncludedLibraries,
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

        var mapped = events
            .Select(e => CloneWithUser(e, ResolveCanonicalUser(e.Source, e.UserDisplayName, aliases)))
            .ToList();

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

        if (filter == WatchStatsSourceFilter.Combined && aliases.Count > 0)
        {
            events = events
                .Select(e => CloneWithUser(e, ResolveCanonicalUser(e.Source, e.UserDisplayName, aliases)))
                .ToList();
        }

        return PlayEventAnalyticsService.Build(events, range, limit);
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
        ProgressPercent = source.ProgressPercent
    };
}
