using ArrDash.Data;
using ArrDash.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

/// <summary>
/// Applies additive schema changes for existing SQLite databases (EnsureCreated does not alter tables).
/// </summary>
public static class DatabaseSchemaUpgrader
{
    private static readonly (string Column, string SqlType)[] PlayEventColumns =
    [
        ("GrandparentExternalId", "TEXT NULL"),
        ("TranscodeDecision", "TEXT NULL"),
        ("LibraryName", "TEXT NULL"),
        ("LibraryExternalId", "TEXT NULL"),
        ("ProgressPercent", "REAL NULL"),
        ("Origin", "TEXT NULL"),
        ("Year", "INTEGER NULL"),
        ("SeasonNumber", "INTEGER NULL"),
        ("EpisodeNumber", "INTEGER NULL"),
        ("ImdbId", "TEXT NULL"),
        ("TmdbId", "INTEGER NULL"),
        ("TvdbId", "INTEGER NULL"),
        ("TraktId", "INTEGER NULL"),
        ("WasCompleted", "INTEGER NOT NULL DEFAULT 1"),
        ("DurationIsEstimated", "INTEGER NOT NULL DEFAULT 0"),
        ("CanonicalMediaKey", "TEXT NULL"),
        ("ItemTitle", "TEXT NULL")
    ];

    private static readonly (string Column, string SqlType)[] MediaInventoryItemColumns =
    [
        ("NeverDelete", "INTEGER NOT NULL DEFAULT 0"),
        ("MarkedForDeletion", "INTEGER NOT NULL DEFAULT 0"),
        ("SeriesStatus", "TEXT NULL"),
        ("Rating", "REAL NULL")
    ];

    public static async Task UpgradeAsync(ArrDashDbContext db, ILogger logger, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (!db.Database.IsSqlite())
            return;

        foreach (var (column, sqlType) in PlayEventColumns)
            await TryAddColumnAsync(db, "PlayEvents", column, sqlType, logger, ct);

        await EnsureTraktTablesAsync(db, ct);
        await EnsureMediaInventoryTableAsync(db, ct);
        await EnsureArrTagsTableAsync(db, ct);

        foreach (var (column, sqlType) in MediaInventoryItemColumns)
            await TryAddColumnAsync(db, "MediaInventoryItems", column, sqlType, logger, ct);
    }

    private static async Task EnsureArrTagsTableAsync(ArrDashDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ArrTags" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ArrTags" PRIMARY KEY AUTOINCREMENT,
                "Source" TEXT NOT NULL,
                "TagId" INTEGER NOT NULL,
                "Label" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ArrTags_Source_TagId"
                ON "ArrTags" ("Source", "TagId");
            """,
            ct);
    }

    private static async Task EnsureMediaInventoryTableAsync(ArrDashDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MediaInventoryItems" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_MediaInventoryItems" PRIMARY KEY AUTOINCREMENT,
                "Source" TEXT NOT NULL,
                "SourceItemId" INTEGER NOT NULL,
                "MediaType" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Year" INTEGER NULL,
                "TitleSlug" TEXT NULL,
                "ImdbId" TEXT NULL,
                "TmdbId" INTEGER NULL,
                "TvdbId" INTEGER NULL,
                "SizeOnDiskBytes" INTEGER NOT NULL,
                "FileCount" INTEGER NULL,
                "Monitored" INTEGER NOT NULL,
                "HasFile" INTEGER NOT NULL,
                "NeverDelete" INTEGER NOT NULL DEFAULT 0,
                "MarkedForDeletion" INTEGER NOT NULL DEFAULT 0,
                "SeriesStatus" TEXT NULL,
                "Rating" REAL NULL,
                "AddedUtc" TEXT NULL,
                "TagsJson" TEXT NOT NULL,
                "LastSeenUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_MediaInventoryItems_Source_SourceItemId"
                ON "MediaInventoryItems" ("Source", "SourceItemId");
            CREATE INDEX IF NOT EXISTS "IX_MediaInventoryItems_TmdbId" ON "MediaInventoryItems" ("TmdbId");
            CREATE INDEX IF NOT EXISTS "IX_MediaInventoryItems_TvdbId" ON "MediaInventoryItems" ("TvdbId");
            CREATE INDEX IF NOT EXISTS "IX_MediaInventoryItems_SizeOnDiskBytes" ON "MediaInventoryItems" ("SizeOnDiskBytes");
            CREATE INDEX IF NOT EXISTS "IX_MediaInventoryItems_LastSeenUtc" ON "MediaInventoryItems" ("LastSeenUtc");
            """,
            ct);
    }

    private static async Task EnsureTraktTablesAsync(ArrDashDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "TraktAccounts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_TraktAccounts" PRIMARY KEY,
                "TraktUsername" TEXT NOT NULL,
                "CanonicalUserName" TEXT NOT NULL,
                "EncryptedAccessToken" TEXT NOT NULL,
                "EncryptedRefreshToken" TEXT NOT NULL,
                "TokenExpiresAtUtc" TEXT NOT NULL,
                "SyncMovies" INTEGER NOT NULL,
                "SyncEpisodes" INTEGER NOT NULL,
                "ImportToWarehouse" INTEGER NOT NULL,
                "PushToTrakt" INTEGER NOT NULL,
                "MarkPlexWatched" INTEGER NOT NULL,
                "MarkEmbyWatched" INTEGER NOT NULL,
                "MarkJellyfinWatched" INTEGER NOT NULL,
                "MappedUsersJson" TEXT NOT NULL,
                "HistoryStartUtc" TEXT NULL,
                "LastSyncedAtUtc" TEXT NULL,
                "LastPreviewAtUtc" TEXT NULL,
                "LastError" TEXT NULL,
                "LastPreviewJson" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_TraktAccounts_TraktUsername" ON "TraktAccounts" ("TraktUsername");

            CREATE TABLE IF NOT EXISTS "TraktHistoryLinks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TraktHistoryLinks" PRIMARY KEY AUTOINCREMENT,
                "AccountId" TEXT NOT NULL,
                "PlayEventId" INTEGER NULL,
                "TraktHistoryId" INTEGER NOT NULL,
                "Direction" TEXT NOT NULL,
                "CanonicalMediaKey" TEXT NOT NULL,
                "LinkedAtUtc" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TraktHistoryLinks_AccountId_TraktHistoryId"
                ON "TraktHistoryLinks" ("AccountId", "TraktHistoryId");
            CREATE INDEX IF NOT EXISTS "IX_TraktHistoryLinks_PlayEventId" ON "TraktHistoryLinks" ("PlayEventId");

            CREATE TABLE IF NOT EXISTS "MediaIdentities" (
                "CanonicalMediaKey" TEXT NOT NULL CONSTRAINT "PK_MediaIdentities" PRIMARY KEY,
                "MediaType" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "SeriesTitle" TEXT NULL,
                "Year" INTEGER NULL,
                "SeasonNumber" INTEGER NULL,
                "EpisodeNumber" INTEGER NULL,
                "ImdbId" TEXT NULL,
                "TmdbId" INTEGER NULL,
                "TvdbId" INTEGER NULL,
                "TraktId" INTEGER NULL,
                "RuntimeSeconds" INTEGER NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_MediaIdentities_ImdbId" ON "MediaIdentities" ("ImdbId");
            CREATE INDEX IF NOT EXISTS "IX_MediaIdentities_TmdbId" ON "MediaIdentities" ("TmdbId");
            CREATE INDEX IF NOT EXISTS "IX_MediaIdentities_TraktId" ON "MediaIdentities" ("TraktId");
            """,
            ct);
    }

    private static async Task TryAddColumnAsync(
        ArrDashDbContext db,
        string table,
        string column,
        string sqlType,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType}",
                ct);
        }
        catch (Exception ex)
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS, so a duplicate-column failure is the
            // expected, silent-on-purpose case for every already-upgraded database on every
            // startup — only genuinely unexpected failures (permissions, corruption, disk
            // full) are worth a Warning.
            if (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
                logger.LogDebug("Column {Table}.{Column} already exists", table, column);
            else
                logger.LogWarning(ex, "Failed to add column {Table}.{Column}", table, column);
        }
    }
}
