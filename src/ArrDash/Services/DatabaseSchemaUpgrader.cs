using ArrDash.Data;
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
        ("ProgressPercent", "REAL NULL")
    ];

    public static async Task UpgradeAsync(ArrDashDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        if (!db.Database.IsSqlite())
            return;

        foreach (var (column, sqlType) in PlayEventColumns)
            await TryAddColumnAsync(db, "PlayEvents", column, sqlType, ct);
    }

    private static async Task TryAddColumnAsync(
        ArrDashDbContext db,
        string table,
        string column,
        string sqlType,
        CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {sqlType}",
                ct);
        }
        catch
        {
            // Column already exists on upgraded databases.
        }
    }
}
