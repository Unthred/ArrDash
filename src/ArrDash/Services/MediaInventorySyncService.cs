using ArrDash.Configuration;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArrDash.Services;

public sealed class MediaInventorySyncService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    RadarrClient radarr,
    SonarrClient sonarr,
    LayoutPreferencesService prefs,
    IOptions<CleanupCandidatesOptions> options,
    ILogger<MediaInventorySyncService> logger) : BackgroundService
{
    private readonly CleanupCandidatesOptions _options = options.Value;
    private readonly object _statusLock = new();
    private MediaInventorySyncStatus _status = new(false, "Waiting for first sync", null);

    public MediaInventorySyncStatus CurrentStatus
    {
        get
        {
            lock (_statusLock)
                return _status;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(35), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Media inventory sync failed");
                PublishStatus(new MediaInventorySyncStatus(false, $"Sync failed: {ex.Message}", _status.LastSyncAt));
            }

            var interval = Math.Clamp(prefs.Current.CleanupCandidatesSyncIntervalMinutes, 15, 720);
            if (prefs.Current.CleanupCandidatesSyncIntervalMinutes <= 0)
                interval = Math.Clamp(_options.SyncIntervalMinutes, 15, 720);

            await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
        }
    }

    public async Task SyncAsync(CancellationToken ct)
    {
        PublishStatus(new MediaInventorySyncStatus(true, "Syncing media inventory…", _status.LastSyncAt));

        var syncStartedUtc = DateTimeOffset.UtcNow;

        if (prefs.IsServiceEnabled("radarr") && radarr.IsConfigured)
            await SyncRadarrAsync(syncStartedUtc, ct);

        if (prefs.IsServiceEnabled("sonarr") && sonarr.IsConfigured)
            await SyncSonarrAsync(syncStartedUtc, ct);

        PublishStatus(new MediaInventorySyncStatus(false, "Sync complete", syncStartedUtc));
    }

    private async Task SyncRadarrAsync(DateTimeOffset syncStartedUtc, CancellationToken ct)
    {
        try
        {
            var movies = await radarr.FetchInventoryAsync(ct);
            var tags = await radarr.FetchTagsAsync(ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            foreach (var movie in movies)
            {
                var existing = await db.MediaInventoryItems.FirstOrDefaultAsync(
                    e => e.Source == "radarr" && e.SourceItemId == movie.Id, ct);

                if (existing is null)
                {
                    existing = new MediaInventoryItemEntity { Source = "radarr", SourceItemId = movie.Id };
                    db.MediaInventoryItems.Add(existing);
                }

                existing.MediaType = "movie";
                existing.Title = movie.Title;
                existing.Year = movie.Year;
                existing.TitleSlug = movie.Slug;
                existing.ImdbId = movie.ImdbId;
                existing.TmdbId = movie.TmdbId;
                existing.TvdbId = null;
                existing.SeriesStatus = null;
                existing.SizeOnDiskBytes = movie.SizeOnDiskBytes;
                existing.FileCount = movie.HasFile ? 1 : 0;
                existing.Monitored = movie.Monitored;
                existing.HasFile = movie.HasFile;
                existing.Rating = movie.Rating;
                existing.AddedUtc = movie.AddedUtc;
                existing.TagsJson = System.Text.Json.JsonSerializer.Serialize(movie.TagIds);
                existing.LastSeenUtc = syncStartedUtc;
                existing.UpdatedAtUtc = syncStartedUtc;
            }

            await UpsertTagsAsync(db, "radarr", tags, syncStartedUtc, ct);
            await db.SaveChangesAsync(ct);
            await DeleteStaleInventoryAsync(db, "radarr", syncStartedUtc, ct);
            await DeleteStaleTagsAsync(db, "radarr", syncStartedUtc, ct);

            logger.LogInformation("Synced {Count} Radarr inventory items and {TagCount} tags", movies.Count, tags.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed syncing Radarr inventory");
        }
    }

    private async Task SyncSonarrAsync(DateTimeOffset syncStartedUtc, CancellationToken ct)
    {
        try
        {
            var series = await sonarr.FetchInventoryAsync(ct);
            var tags = await sonarr.FetchTagsAsync(ct);
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            foreach (var show in series)
            {
                var existing = await db.MediaInventoryItems.FirstOrDefaultAsync(
                    e => e.Source == "sonarr" && e.SourceItemId == show.Id, ct);

                if (existing is null)
                {
                    existing = new MediaInventoryItemEntity { Source = "sonarr", SourceItemId = show.Id };
                    db.MediaInventoryItems.Add(existing);
                }

                existing.MediaType = "series";
                existing.Title = show.Title;
                existing.Year = show.Year;
                existing.TitleSlug = show.Slug;
                existing.ImdbId = show.ImdbId;
                existing.TmdbId = null;
                existing.TvdbId = show.TvdbId;
                existing.SeriesStatus = show.SeriesStatus;
                existing.SizeOnDiskBytes = show.SizeOnDiskBytes;
                existing.FileCount = show.EpisodeFileCount;
                existing.Monitored = show.Monitored;
                existing.HasFile = show.HasFile;
                existing.Rating = show.Rating;
                existing.AddedUtc = show.AddedUtc;
                existing.TagsJson = System.Text.Json.JsonSerializer.Serialize(show.TagIds);
                existing.LastSeenUtc = syncStartedUtc;
                existing.UpdatedAtUtc = syncStartedUtc;
            }

            await UpsertTagsAsync(db, "sonarr", tags, syncStartedUtc, ct);
            await db.SaveChangesAsync(ct);
            await DeleteStaleInventoryAsync(db, "sonarr", syncStartedUtc, ct);
            await DeleteStaleTagsAsync(db, "sonarr", syncStartedUtc, ct);

            logger.LogInformation("Synced {Count} Sonarr inventory items and {TagCount} tags", series.Count, tags.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed syncing Sonarr inventory");
        }
    }

    private static async Task UpsertTagsAsync(
        ArrDashDbContext db,
        string source,
        IReadOnlyList<ArrTagDto> tags,
        DateTimeOffset syncStartedUtc,
        CancellationToken ct)
    {
        foreach (var tag in tags)
        {
            var existing = await db.ArrTags.FirstOrDefaultAsync(
                e => e.Source == source && e.TagId == tag.Id, ct);

            if (existing is null)
            {
                db.ArrTags.Add(new ArrTagEntity
                {
                    Source = source,
                    TagId = tag.Id,
                    Label = tag.Label,
                    UpdatedAtUtc = syncStartedUtc
                });
                continue;
            }

            existing.Label = tag.Label;
            existing.UpdatedAtUtc = syncStartedUtc;
        }
    }

    // SQLite + EF cannot translate DateTimeOffset comparisons inside ExecuteDelete, so we
    // resolve stale ids in memory (inventory is a few thousand rows at most) then delete by PK.
    private static async Task DeleteStaleInventoryAsync(
        ArrDashDbContext db,
        string source,
        DateTimeOffset syncStartedUtc,
        CancellationToken ct)
    {
        var staleIds = (await db.MediaInventoryItems.AsNoTracking()
                .Where(e => e.Source == source)
                .Select(e => new { e.Id, e.LastSeenUtc })
                .ToListAsync(ct))
            .Where(e => e.LastSeenUtc < syncStartedUtc)
            .Select(e => e.Id)
            .ToList();

        if (staleIds.Count == 0)
            return;

        await db.MediaInventoryItems.Where(e => staleIds.Contains(e.Id)).ExecuteDeleteAsync(ct);
    }

    private static async Task DeleteStaleTagsAsync(
        ArrDashDbContext db,
        string source,
        DateTimeOffset syncStartedUtc,
        CancellationToken ct)
    {
        var staleIds = (await db.ArrTags.AsNoTracking()
                .Where(e => e.Source == source)
                .Select(e => new { e.Id, e.UpdatedAtUtc })
                .ToListAsync(ct))
            .Where(e => e.UpdatedAtUtc < syncStartedUtc)
            .Select(e => e.Id)
            .ToList();

        if (staleIds.Count == 0)
            return;

        await db.ArrTags.Where(e => staleIds.Contains(e.Id)).ExecuteDeleteAsync(ct);
    }

    private void PublishStatus(MediaInventorySyncStatus status)
    {
        lock (_statusLock)
            _status = status;
    }
}
