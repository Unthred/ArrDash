using System.Text.Json;
using System.Text.Json.Serialization;
using ArrDash.Models;
using Microsoft.Extensions.Options;
using ArrDash.Configuration;

namespace ArrDash.Services;

public sealed class WatchStatsSnapshotFileCache(IOptions<DatabaseOptions> dbOptions)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly string _root = Path.Combine(ResolveConfigRoot(dbOptions.Value.SqlitePath), "cache", "watch-stats");

    public static string BuildKey(WatchStatsRange range, WatchStatsSourceFilter filter) =>
        ActivitySnapshotFileCache.BuildKey(range, filter);

    public async Task<WatchStatsCachedEnvelope?> TryGetAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var envelope = await JsonSerializer.DeserializeAsync<CacheEnvelope<WatchStatsSnapshot>>(stream, JsonOptions, ct);
            if (envelope?.Snapshot is null)
                return null;

            return new WatchStatsCachedEnvelope(envelope.Snapshot, envelope.UpdatedAtUtc);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string key, WatchStatsSnapshot snapshot, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(key);
        var temp = path + ".tmp";
        var envelope = new CacheEnvelope<WatchStatsSnapshot>(snapshot, DateTimeOffset.UtcNow);

        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, envelope, JsonOptions, ct);

        File.Move(temp, path, overwrite: true);
    }

    private string PathFor(string key)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_root, $"{safe}.json");
    }

    private static string ResolveConfigRoot(string sqlitePath)
    {
        var configRoot = Environment.GetEnvironmentVariable("ARRDASH_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(configRoot))
            return configRoot;

        if (Path.IsPathRooted(sqlitePath))
            return Path.GetDirectoryName(sqlitePath) ?? "/config";

        return "/config";
    }

    private sealed record CacheEnvelope<T>(T Snapshot, DateTimeOffset UpdatedAtUtc);

    public sealed record WatchStatsCachedEnvelope(WatchStatsSnapshot Snapshot, DateTimeOffset UpdatedAtUtc)
    {
        public TimeSpan Age => DateTimeOffset.UtcNow - UpdatedAtUtc;
    }
}
