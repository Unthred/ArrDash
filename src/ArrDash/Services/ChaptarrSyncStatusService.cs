using System.Text.Json;

namespace ArrDash.Services;

public sealed record ChaptarrSyncConflictField(string Field, string AbsValue, string ChaptarrValue);

public sealed record ChaptarrSyncConflictItem(string RelPath, IReadOnlyList<ChaptarrSyncConflictField> Fields);

public sealed record ChaptarrSyncStatus(
    DateTimeOffset? LastRunAt,
    int FilledCount,
    int ConflictCount,
    IReadOnlyList<ChaptarrSyncConflictItem> Conflicts,
    int CollectionsCreated,
    int CollectionsAddedTo,
    int CollectionsLikelyDuplicate)
{
    public static readonly ChaptarrSyncStatus Empty = new(null, 0, 0, [], 0, 0, 0);

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(90);

    public bool IsStale => LastRunAt is null || DateTimeOffset.UtcNow - LastRunAt > StaleAfter;

    public string Summary
    {
        get
        {
            if (LastRunAt is null)
                return "No sync run recorded yet.";

            var parts = new List<string>
            {
                FilledCount switch
                {
                    0 => "no new fills",
                    1 => "filled 1 book",
                    _ => $"filled {FilledCount} books"
                }
            };

            if (ConflictCount > 0)
                parts.Add($"{ConflictCount} conflict{(ConflictCount == 1 ? "" : "s")} need{(ConflictCount == 1 ? "s" : "")} review");

            if (CollectionsCreated > 0)
                parts.Add($"{CollectionsCreated} new collection{(CollectionsCreated == 1 ? "" : "s")}");

            var text = string.Join(", ", parts);
            return char.ToUpperInvariant(text[0]) + text[1..] + ".";
        }
    }
}

/// <summary>
/// Reads status from the JSON reports written by the external hourly cron job at
/// /mnt/user/src/scripts/chaptarr-abs-sync/{sync,collections}.js (not managed by ArrDash --
/// see project_chaptarr_abs_sync memory). Report files are re-read only when their mtime
/// changes, mirroring LibraryStatsService's cache-with-TTL style but keyed on file freshness
/// rather than a fixed timer, since the data's cadence is external to this app.
/// </summary>
public sealed class ChaptarrSyncStatusService(ILogger<ChaptarrSyncStatusService> logger)
{
    private const string BaseDir = "/mnt/user/src/scripts/chaptarr-abs-sync";
    private static readonly string SyncReportPath = Path.Combine(BaseDir, "last-apply-report.json");
    private static readonly string CollectionsReportPath = Path.Combine(BaseDir, "collections-report.json");

    private readonly object _lock = new();
    private ChaptarrSyncStatus _cached = ChaptarrSyncStatus.Empty;
    private DateTime _syncMtime;
    private DateTime _collectionsMtime;
    private bool _hasReadOnce;

    public Task<ChaptarrSyncStatus> GetAsync(CancellationToken ct = default)
    {
        var syncMtime = SafeGetMtime(SyncReportPath);
        var collectionsMtime = SafeGetMtime(CollectionsReportPath);

        lock (_lock)
        {
            if (_hasReadOnce && syncMtime == _syncMtime && collectionsMtime == _collectionsMtime)
                return Task.FromResult(_cached);
        }

        var status = ReadReports(syncMtime, collectionsMtime);

        lock (_lock)
        {
            _cached = status;
            _syncMtime = syncMtime;
            _collectionsMtime = collectionsMtime;
            _hasReadOnce = true;
        }

        return Task.FromResult(status);
    }

    private static DateTime SafeGetMtime(string path) =>
        File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;

    private ChaptarrSyncStatus ReadReports(DateTime syncMtime, DateTime collectionsMtime)
    {
        var filled = 0;
        var conflicts = new List<ChaptarrSyncConflictItem>();
        DateTimeOffset? lastRun = null;

        if (syncMtime != default)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SyncReportPath));
                var root = doc.RootElement;

                if (root.TryGetProperty("filled", out var filledArr))
                    filled = filledArr.GetArrayLength();

                if (root.TryGetProperty("conflicts", out var conflictsArr))
                {
                    foreach (var item in conflictsArr.EnumerateArray())
                    {
                        var relPath = item.TryGetProperty("relPath", out var rp) ? rp.GetString() ?? "" : "";
                        var fields = new List<ChaptarrSyncConflictField>();
                        if (item.TryGetProperty("conflicts", out var fieldArr))
                        {
                            foreach (var f in fieldArr.EnumerateArray())
                            {
                                fields.Add(new ChaptarrSyncConflictField(
                                    f.TryGetProperty("field", out var fv) ? fv.GetString() ?? "" : "",
                                    f.TryGetProperty("abs", out var av) ? av.GetString() ?? "" : "",
                                    f.TryGetProperty("chaptarr", out var cv) ? cv.GetString() ?? "" : ""));
                            }
                        }
                        conflicts.Add(new ChaptarrSyncConflictItem(relPath, fields));
                    }
                }

                lastRun = new DateTimeOffset(syncMtime, TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse {Path} (may be mid-write); keeping previous status", SyncReportPath);
                return _cached;
            }
        }

        var created = 0;
        var addedTo = 0;
        var likelyDuplicate = 0;

        if (collectionsMtime != default)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(CollectionsReportPath));
                var root = doc.RootElement;

                if (root.TryGetProperty("created", out var c)) created = c.GetArrayLength();
                if (root.TryGetProperty("addedTo", out var a)) addedTo = a.GetArrayLength();
                if (root.TryGetProperty("likelyDuplicate", out var d)) likelyDuplicate = d.GetArrayLength();

                var collectionsRunAt = new DateTimeOffset(collectionsMtime, TimeSpan.Zero);
                if (lastRun is null || collectionsRunAt > lastRun)
                    lastRun = collectionsRunAt;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse {Path} (may be mid-write); keeping previous collections status", CollectionsReportPath);
            }
        }

        return new ChaptarrSyncStatus(lastRun, filled, conflicts.Count, conflicts, created, addedTo, likelyDuplicate);
    }
}
