using ArrDash.Configuration;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArrDash.Services;

public sealed class WatchHistorySyncService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TautulliClient tautulli,
    PlexHistoryClient plexHistory,
    TracearrClient tracearr,
    EmbyPlaybackReportingClient emby,
    JellyfinPlaybackReportingClient jellyfin,
    LayoutPreferencesService prefs,
    IOptions<WatchStatsOptions> watchStatsOptions,
    ILogger<WatchHistorySyncService> logger) : BackgroundService
{
    private readonly WatchStatsOptions _options = watchStatsOptions.Value;
    private readonly object _statusLock = new();
    private WatchStatsSyncStatus _status = CreateIdleStatus();

    public event Action? StatusChanged;

    public WatchStatsSyncStatus CurrentStatus
    {
        get
        {
            lock (_statusLock)
                return _status;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (prefs.Current.WatchStatsSyncEnabled)
                    await SyncAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Watch history sync failed");
                PublishStatus(_status with
                {
                    IsRunning = false,
                    CurrentMessage = $"Sync failed: {ex.Message}"
                });
            }

            var interval = Math.Clamp(prefs.Current.WatchStatsSyncIntervalMinutes, 5, 240);
            if (interval <= 0)
                interval = Math.Clamp(_options.SyncIntervalMinutes, 5, 240);

            await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
        }
    }

    public async Task SyncAsync(CancellationToken ct)
    {
        var planned = BuildPlannedSources();
        var sourceStates = planned.ToDictionary(
            s => s.Key,
            s => new WatchStatsSourceSyncProgress(
                s.Key,
                s.Label,
                WatchStatsSourceSyncState.Pending,
                s.SkipReason ?? "Waiting",
                0,
                0,
                null,
                null));

        PublishStatus(new WatchStatsSyncStatus(
            IsRunning: true,
            CurrentMessage: "Starting watch history sync…",
            ProgressPercent: 0,
            StartedAt: DateTimeOffset.UtcNow,
            LastSyncAt: _status.LastSyncAt,
            TotalEvents: _status.TotalEvents,
            Sources: sourceStates.Values.ToList(),
            SourceErrors: new Dictionary<string, string?>()));

        var errors = new Dictionary<string, string?>();
        DateTimeOffset? lastSync = _status.LastSyncAt;
        var active = planned.Where(s => s.Enabled).ToList();
        var completed = 0;

        foreach (var plan in planned)
        {
            if (!plan.Enabled)
            {
                sourceStates[plan.Key] = sourceStates[plan.Key] with
                {
                    State = WatchStatsSourceSyncState.Skipped,
                    Message = plan.SkipReason ?? "Skipped"
                };
                PublishRunning(sourceStates, active.Count, completed, $"Skipped {plan.Label}");
                continue;
            }

            sourceStates[plan.Key] = sourceStates[plan.Key] with
            {
                State = WatchStatsSourceSyncState.Running,
                Message = "Fetching history…"
            };
            PublishRunning(sourceStates, active.Count, completed, $"Syncing {plan.Label}…");

            var (error, syncedAt, imported, fetched) = await SyncSourceAsync(
                plan.Key,
                plan.Fetch!,
                message => UpdateSourceMessage(sourceStates, plan.Key, message),
                ct);

            errors[plan.Key] = error;
            lastSync = Max(lastSync, syncedAt);
            completed++;

            sourceStates[plan.Key] = sourceStates[plan.Key] with
            {
                State = error is null ? WatchStatsSourceSyncState.Completed : WatchStatsSourceSyncState.Failed,
                Message = error is null
                    ? imported > 0 ? $"Imported {imported} new events" : "Up to date"
                    : error,
                ImportedCount = imported,
                FetchedCount = fetched,
                LastSyncedAt = syncedAt,
                Error = error
            };

            PublishRunning(sourceStates, active.Count, completed, error is null
                ? $"{plan.Label} complete"
                : $"{plan.Label} failed");
        }

        PublishRunning(sourceStates, active.Count, completed, "Cleaning up old events…");
        await PruneOldEventsAsync(ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var total = await db.PlayEvents.LongCountAsync(ct);

        PublishStatus(new WatchStatsSyncStatus(
            IsRunning: false,
            CurrentMessage: errors.Values.Any(e => e is not null)
                ? "Sync finished with errors"
                : "Sync complete",
            ProgressPercent: 100,
            StartedAt: null,
            LastSyncAt: lastSync ?? _status.LastSyncAt,
            TotalEvents: total,
            Sources: sourceStates.Values.ToList(),
            SourceErrors: errors));
    }

    private async Task<(string? Error, DateTimeOffset? SyncedAt, int Imported, int Fetched)> SyncSourceAsync(
        string source,
        Func<DateTimeOffset, bool, CancellationToken, Task<IReadOnlyList<ImportedPlayEvent>>> fetch,
        Action<string> report,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cursor = await db.SyncCursors.FindAsync([source], ct);
        if (cursor is null)
        {
            cursor = new SyncCursorEntity { Source = source };
            db.SyncCursors.Add(cursor);
        }

        cursor.LastAttemptAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var backfillDays = WatchStatsBackfillHelper.Resolve(prefs.Current.WatchStatsBackfillDays, _options.BackfillDays);
            var isInitial = cursor.LastSyncedAtUtc is null;
            var since = cursor.LastSyncedAtUtc?.AddDays(-1)
                ?? DateTimeOffset.UtcNow.AddDays(-backfillDays);

            report(isInitial ? "Backfilling history…" : "Fetching new history…");
            var events = await fetch(since, isInitial, ct);
            var fetched = events.Count;
            var imported = 0;

            report(fetched == 0 ? "No new history found" : $"Importing {fetched} events…");

            foreach (var batch in events.Chunk(200))
            {
                foreach (var ev in batch)
                {
                    var exists = await db.PlayEvents.AnyAsync(
                        e => e.Source == ev.Source && e.ExternalPlayId == ev.ExternalPlayId, ct);
                    if (exists)
                        continue;

                    db.PlayEvents.Add(PlayEventImportHelper.MapEntity(ev));
                    imported++;
                }

                await db.SaveChangesAsync(ct);
                if (imported > 0)
                    report($"Imported {imported} new events…");
            }

            cursor.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            cursor.LastError = null;
            cursor.LastExternalId = events.FirstOrDefault()?.ExternalPlayId;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Imported {Count} watch events from {Source}", imported, source);
            return (null, cursor.LastSyncedAtUtc, imported, fetched);
        }
        catch (Exception ex)
        {
            cursor.LastError = ex.Message;
            db.SyncCursors.Update(cursor);
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "Failed syncing watch history for {Source}", source);
            return (ex.Message, cursor.LastSyncedAtUtc, 0, 0);
        }
    }

    private IReadOnlyList<PlannedSource> BuildPlannedSources()
    {
        var list = new List<PlannedSource>();

        if (prefs.Current.WatchStatsSyncPlex)
        {
            if (!prefs.IsServiceEnabled("plex"))
            {
                list.Add(new PlannedSource(WatchStatsSources.Plex, WatchStatsSources.Label(WatchStatsSources.Plex), false, "Plex disabled in Settings", null));
            }
            else if (tautulli.IsConfigured)
            {
                list.Add(new PlannedSource(WatchStatsSources.Plex, $"{WatchStatsSources.Label(WatchStatsSources.Plex)} (Tautulli bootstrap)", true, null, FetchPlexTautulliAsync));
            }
            else if (plexHistory.IsConfigured)
            {
                list.Add(new PlannedSource(WatchStatsSources.Plex, $"{WatchStatsSources.Label(WatchStatsSources.Plex)} (PMS API)", true, null, FetchPlexNativeAsync));
            }
            else
            {
                list.Add(new PlannedSource(WatchStatsSources.Plex, WatchStatsSources.Label(WatchStatsSources.Plex), false, "Configure Plex token or Tautulli for history sync", null));
            }
        }

        if (prefs.Current.WatchStatsSyncEmby)
        {
            if (!prefs.IsServiceEnabled("emby"))
                list.Add(new PlannedSource(WatchStatsSources.Emby, WatchStatsSources.Label(WatchStatsSources.Emby), false, "Emby disabled in Settings", null));
            else if (tracearr.IsConfigured)
                list.Add(new PlannedSource(WatchStatsSources.Emby, $"{WatchStatsSources.Label(WatchStatsSources.Emby)} (Tracearr bootstrap)", true, null, (s, i, c) => FetchTracearrAsync(WatchStatsSources.Emby, s, i, c)));
            else if (emby.IsConfigured)
                list.Add(new PlannedSource(WatchStatsSources.Emby, $"{WatchStatsSources.Label(WatchStatsSources.Emby)} (Playback Reporting)", true, null, (s, _, c) => emby.FetchHistoryAsync(s, c)));
            else
                list.Add(new PlannedSource(WatchStatsSources.Emby, WatchStatsSources.Label(WatchStatsSources.Emby), false, "Emby not configured", null));
        }

        if (prefs.Current.WatchStatsSyncJellyfin)
        {
            if (!prefs.IsServiceEnabled("jellyfin"))
                list.Add(new PlannedSource(WatchStatsSources.Jellyfin, WatchStatsSources.Label(WatchStatsSources.Jellyfin), false, "Jellyfin disabled in Settings", null));
            else if (tracearr.IsConfigured)
                list.Add(new PlannedSource(WatchStatsSources.Jellyfin, $"{WatchStatsSources.Label(WatchStatsSources.Jellyfin)} (Tracearr bootstrap)", true, null, (s, i, c) => FetchTracearrAsync(WatchStatsSources.Jellyfin, s, i, c)));
            else if (jellyfin.IsConfigured)
                list.Add(new PlannedSource(WatchStatsSources.Jellyfin, $"{WatchStatsSources.Label(WatchStatsSources.Jellyfin)} (Playback Reporting)", true, null, (s, _, c) => jellyfin.FetchHistoryAsync(s, c)));
            else
                list.Add(new PlannedSource(WatchStatsSources.Jellyfin, WatchStatsSources.Label(WatchStatsSources.Jellyfin), false, "Jellyfin not configured", null));
        }

        return list;
    }

    private async Task<IReadOnlyList<ImportedPlayEvent>> FetchPlexTautulliAsync(
        DateTimeOffset sinceUtc,
        bool isInitial,
        CancellationToken ct)
    {
        var backfillDays = WatchStatsBackfillHelper.Resolve(prefs.Current.WatchStatsBackfillDays, _options.BackfillDays);
        var maxRows = isInitial
            ? Math.Min(backfillDays * 500, 50_000)
            : Math.Min(backfillDays * 200, 5_000);
        return await tautulli.FetchHistoryAsync(sinceUtc, maxRows, ct);
    }

    private async Task<IReadOnlyList<ImportedPlayEvent>> FetchPlexNativeAsync(
        DateTimeOffset sinceUtc,
        bool isInitial,
        CancellationToken ct)
    {
        var backfillDays = WatchStatsBackfillHelper.Resolve(prefs.Current.WatchStatsBackfillDays, _options.BackfillDays);
        var maxRows = isInitial
            ? Math.Min(backfillDays * 500, 50_000)
            : Math.Min(backfillDays * 200, 5_000);
        return await plexHistory.FetchHistoryAsync(sinceUtc, maxRows, ct);
    }

    private async Task<IReadOnlyList<ImportedPlayEvent>> FetchTracearrAsync(
        string sourceKey,
        DateTimeOffset sinceUtc,
        bool isInitial,
        CancellationToken ct)
    {
        var backfillDays = WatchStatsBackfillHelper.Resolve(prefs.Current.WatchStatsBackfillDays, _options.BackfillDays);
        var maxRows = isInitial
            ? Math.Min(backfillDays * 500, 50_000)
            : Math.Min(backfillDays * 200, 5_000);
        return await tracearr.FetchImportedHistoryAsync(sourceKey, sinceUtc, maxRows, ct);
    }

    private void PublishRunning(
        Dictionary<string, WatchStatsSourceSyncProgress> sourceStates,
        int activeCount,
        int completedCount,
        string message)
    {
        var percent = activeCount == 0
            ? 100
            : (int)Math.Clamp(completedCount * 100.0 / activeCount, 0, 99);

        PublishStatus(new WatchStatsSyncStatus(
            IsRunning: true,
            CurrentMessage: message,
            ProgressPercent: percent,
            StartedAt: _status.StartedAt ?? DateTimeOffset.UtcNow,
            LastSyncAt: _status.LastSyncAt,
            TotalEvents: _status.TotalEvents,
            Sources: sourceStates.Values.ToList(),
            SourceErrors: _status.SourceErrors));
    }

    private static void UpdateSourceMessage(
        Dictionary<string, WatchStatsSourceSyncProgress> sourceStates,
        string key,
        string message)
    {
        if (sourceStates.TryGetValue(key, out var current))
            sourceStates[key] = current with { Message = message };
    }

    private void PublishStatus(WatchStatsSyncStatus status)
    {
        lock (_statusLock)
            _status = status;
        StatusChanged?.Invoke();
    }

    private static WatchStatsSyncStatus CreateIdleStatus() => new(
        IsRunning: false,
        CurrentMessage: "Waiting for first sync",
        ProgressPercent: 0,
        StartedAt: null,
        LastSyncAt: null,
        TotalEvents: 0,
        Sources: [],
        SourceErrors: new Dictionary<string, string?>());

    private async Task PruneOldEventsAsync(CancellationToken ct)
    {
        var retentionDays = WatchStatsBackfillHelper.ResolveRetention(
            prefs.Current.WatchStatsRetentionDays,
            _options.RetentionDays);

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Trakt history is bounded by the account's HistoryStartUtc, not server retention;
        // pruning it would silently erase the imported backlog (#36).
        await db.PlayEvents
            .Where(e => e.PlayedAtUtc < cutoff && e.Source != WatchStatsSources.Trakt)
            .ExecuteDeleteAsync(ct);
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null)
            return b;
        if (b is null)
            return a;
        return a > b ? a : b;
    }

    private sealed record PlannedSource(
        string Key,
        string Label,
        bool Enabled,
        string? SkipReason,
        Func<DateTimeOffset, bool, CancellationToken, Task<IReadOnlyList<ImportedPlayEvent>>>? Fetch);
}
