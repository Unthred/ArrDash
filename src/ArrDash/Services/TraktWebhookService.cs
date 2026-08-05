using System.Text.Json;
using ArrDash.Data;
using ArrDash.Data.Entities;
using ArrDash.Models;
using ArrDash.Services.Clients;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

/// <summary>
/// Live Emby/Plex play webhooks → Trakt scrobble/stop + collection (Squiggley only).
/// </summary>
public sealed class TraktWebhookService(
    IDbContextFactory<ArrDashDbContext> dbFactory,
    TraktAccountService accounts,
    TraktClient trakt,
    EmbyPlaybackReportingClient emby,
    ILogger<TraktWebhookService> logger)
{
    public async Task<(bool Ok, string Message)> HandleEmbyAsync(JsonElement payload, CancellationToken ct)
    {
        var eventName = ReadString(payload, "Event", "NotificationType", "event") ?? "";
        if (!eventName.Contains("playback.stop", StringComparison.OrdinalIgnoreCase)
            && !eventName.Contains("PlaybackStop", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(eventName, "item.markplayed", StringComparison.OrdinalIgnoreCase))
        {
            return (true, $"Ignored event '{eventName}'");
        }

        string? userName = null;
        if (payload.TryGetProperty("User", out var userEl) && userEl.ValueKind == JsonValueKind.Object)
            userName = ReadString(userEl, "Name", "name");
        userName ??= ReadString(payload, "UserName", "user_name");

        JsonElement item = default;
        var hasItem = payload.TryGetProperty("Item", out item) && item.ValueKind == JsonValueKind.Object;
        if (!hasItem && payload.TryGetProperty("item", out item) && item.ValueKind == JsonValueKind.Object)
            hasItem = true;
        if (!hasItem)
            return (false, "Missing Item in Emby webhook payload");

        var progress = EstimateProgress(payload, item);
        if (progress < 80
            && !IsPlayedFlag(item)
            && !eventName.Contains("markplayed", StringComparison.OrdinalIgnoreCase))
            return (true, $"Ignored incomplete play ({progress:0}%)");

        return await ScrobbleItemAsync(
            userName,
            ParseFromEmbyItem(item),
            Math.Max(progress, 90),
            ct);
    }

    public async Task<(bool Ok, string Message)> HandlePlexAsync(JsonElement payload, CancellationToken ct)
    {
        var eventName = ReadString(payload, "event") ?? "";
        if (!string.Equals(eventName, "media.scrobble", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(eventName, "media.rate", StringComparison.OrdinalIgnoreCase))
        {
            // media.scrobble is the completed-play event; ignore others quietly.
            if (!string.Equals(eventName, "media.play", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(eventName, "media.stop", StringComparison.OrdinalIgnoreCase))
                return (true, $"Ignored event '{eventName}'");
            return (true, $"Ignored event '{eventName}'");
        }

        if (!string.Equals(eventName, "media.scrobble", StringComparison.OrdinalIgnoreCase))
            return (true, $"Ignored event '{eventName}'");

        string? userName = null;
        if (payload.TryGetProperty("Account", out var accountEl) && accountEl.ValueKind == JsonValueKind.Object)
            userName = ReadString(accountEl, "title", "username");

        if (!payload.TryGetProperty("Metadata", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return (false, "Missing Metadata in Plex webhook payload");

        return await ScrobbleItemAsync(userName, ParseFromPlexMetadata(meta), 90, ct);
    }

    private async Task<(bool Ok, string Message)> ScrobbleItemAsync(
        string? userName,
        LibraryWatchedItem? item,
        double progress,
        CancellationToken ct)
    {
        if (item is null)
            return (false, "Could not parse media item from webhook");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var account = await db.TraktAccounts.AsNoTracking().FirstOrDefaultAsync(ct);
        if (account is null)
            return (false, "No Trakt account connected");

        if (!UserMatches(account, userName))
            return (true, $"Ignored user '{userName}' (not mapped to {account.CanonicalUserName})");

        if (item.MediaType == "movie" && !account.SyncMovies)
            return (true, "Movies sync disabled");
        if (item.MediaType == "episode" && !account.SyncEpisodes)
            return (true, "Episodes sync disabled");

        // If webhook payload lacked provider ids, try Emby item fetch.
        if (LibraryWatchedToTraktService.BuildIds(item) is null
            && string.Equals(item.Source, WatchStatsSources.Emby, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.ServerItemId)
            && emby.IsConfigured)
        {
            var info = await emby.FetchItemProviderInfoAsync([item.ServerItemId], ct);
            if (info.TryGetValue(item.ServerItemId, out var enriched))
            {
                item = item with
                {
                    ImdbId = enriched.ImdbId ?? item.ImdbId,
                    TmdbId = enriched.TmdbId ?? item.TmdbId,
                    TvdbId = enriched.TvdbId ?? item.TvdbId,
                    TraktId = enriched.TraktId ?? item.TraktId,
                    SeasonNumber = enriched.SeasonNumber ?? item.SeasonNumber,
                    EpisodeNumber = enriched.EpisodeNumber ?? item.EpisodeNumber,
                    Year = enriched.Year ?? item.Year
                };
            }
        }

        var ids = LibraryWatchedToTraktService.BuildIds(item);
        if (ids is null)
            return (false, $"No provider ids for '{item.Title}' — cannot scrobble to Trakt");

        var (accessToken, _) = await accounts.GetValidAccessTokenAsync(account.Id, ct);

        object body = item.MediaType == "movie"
            ? new { movie = new { ids }, progress }
            : item.SeasonNumber is int sn && item.EpisodeNumber is int en
                ? new { show = new { ids }, episode = new { season = sn, number = en }, progress }
                : new { episode = new { ids }, progress };

        var scrobble = await trakt.ScrobbleStopAsync(accessToken, body, ct);
        if (scrobble is null)
            return (false, "Trakt scrobble/stop failed");

        // Best-effort collection add for the same item.
        try
        {
            if (item.MediaType == "movie")
                await trakt.AddToCollectionAsync(accessToken, new { movies = new[] { new { ids } } }, ct);
            else if (item.SeasonNumber is int sn2 && item.EpisodeNumber is int en2)
            {
                await trakt.AddToCollectionAsync(accessToken, new
                {
                    shows = new[]
                    {
                        new
                        {
                            ids,
                            seasons = new[]
                            {
                                new { number = sn2, episodes = new[] { new { number = en2 } } }
                            }
                        }
                    }
                }, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Webhook collection add failed for {Title}", item.Title);
        }

        logger.LogInformation(
            "Webhook scrobbled {Type} '{Title}' for {User} (progress {Progress})",
            item.MediaType, item.Title, account.CanonicalUserName, progress);
        return (true, $"Scrobbled {item.MediaType} '{item.Title}'");
    }

    private static bool UserMatches(TraktAccountEntity account, string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return false;
        if (string.Equals(userName, account.CanonicalUserName, StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var mapped = JsonSerializer.Deserialize<List<TraktMappedUser>>(account.MappedUsersJson) ?? [];
            return mapped.Any(m => string.Equals(m.UserName, userName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static LibraryWatchedItem? ParseFromEmbyItem(JsonElement item)
    {
        var id = ReadString(item, "Id") ?? "";
        var typeRaw = ReadString(item, "Type") ?? "";
        var mediaType = typeRaw.Equals("Episode", StringComparison.OrdinalIgnoreCase) ? "episode"
            : typeRaw.Equals("Movie", StringComparison.OrdinalIgnoreCase) ? "movie"
            : null;
        if (mediaType is null)
            return null;

        string? imdb = null;
        int? tmdb = null;
        int? tvdb = null;
        int? traktId = null;
        if (item.TryGetProperty("ProviderIds", out var providers) && providers.ValueKind == JsonValueKind.Object)
        {
            imdb = ReadProviderString(providers, "Imdb", "IMDB", "imdb");
            tmdb = ReadProviderInt(providers, "Tmdb", "TMDb", "tmdb");
            tvdb = ReadProviderInt(providers, "Tvdb", "TVDB", "tvdb");
            traktId = ReadProviderInt(providers, "Trakt", "trakt");
        }

        var title = ReadString(item, "Name") ?? "Unknown";
        var series = ReadString(item, "SeriesName");
        return new LibraryWatchedItem(
            WatchStatsSources.Emby,
            "",
            id,
            mediaType,
            mediaType == "episode" && !string.IsNullOrWhiteSpace(series) ? series! : title,
            series,
            imdb,
            tmdb,
            tvdb,
            traktId,
            ReadInt(item, "ProductionYear"),
            ReadInt(item, "ParentIndexNumber"),
            ReadInt(item, "IndexNumber"),
            DateTimeOffset.UtcNow,
            null);
    }

    private static LibraryWatchedItem? ParseFromPlexMetadata(JsonElement meta)
    {
        var typeRaw = ReadString(meta, "type") ?? "";
        var mediaType = typeRaw.Equals("episode", StringComparison.OrdinalIgnoreCase) ? "episode"
            : typeRaw.Equals("movie", StringComparison.OrdinalIgnoreCase) ? "movie"
            : null;
        if (mediaType is null)
            return null;

        string? imdb = null;
        int? tmdb = null;
        int? tvdb = null;
        if (meta.TryGetProperty("Guid", out var guids) && guids.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in guids.EnumerateArray())
            {
                var id = g.ValueKind == JsonValueKind.Object ? ReadString(g, "id") : g.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (id.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
                    imdb ??= id["imdb://".Length..];
                else if (id.StartsWith("tmdb://", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(id["tmdb://".Length..], out var tmdbId))
                    tmdb ??= tmdbId;
                else if (id.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(id["tvdb://".Length..], out var tvdbId))
                    tvdb ??= tvdbId;
            }
        }

        // Older payloads put a single guid string on Metadata.guid
        var singleGuid = ReadString(meta, "guid");
        if (!string.IsNullOrWhiteSpace(singleGuid))
        {
            if (singleGuid.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
                imdb ??= singleGuid["imdb://".Length..];
        }

        var title = ReadString(meta, "title") ?? "Unknown";
        var series = ReadString(meta, "grandparentTitle");
        return new LibraryWatchedItem(
            WatchStatsSources.Plex,
            "",
            ReadString(meta, "ratingKey") ?? "",
            mediaType,
            mediaType == "episode" && !string.IsNullOrWhiteSpace(series) ? series! : title,
            series,
            imdb,
            tmdb,
            tvdb,
            null,
            ReadInt(meta, "year"),
            ReadInt(meta, "parentIndex"),
            ReadInt(meta, "index"),
            DateTimeOffset.UtcNow,
            null);
    }

    private static double EstimateProgress(JsonElement payload, JsonElement item)
    {
        long? pos = null;
        long? run = null;
        if (payload.TryGetProperty("PlaybackInfo", out var pb) && pb.ValueKind == JsonValueKind.Object)
        {
            pos = ReadLong(pb, "PositionTicks", "position_ticks");
            run = ReadLong(pb, "RunTimeTicks", "runtime_ticks");
        }

        if (item.TryGetProperty("UserData", out var ud) && ud.ValueKind == JsonValueKind.Object)
            pos ??= ReadLong(ud, "PlaybackPositionTicks");

        run ??= ReadLong(item, "RunTimeTicks");
        if (pos is > 0 && run is > 0)
            return 100.0 * pos.Value / run.Value;
        return IsPlayedFlag(item) ? 100 : 0;
    }

    private static bool IsPlayedFlag(JsonElement item) =>
        item.TryGetProperty("UserData", out var ud)
        && ud.ValueKind == JsonValueKind.Object
        && ud.TryGetProperty("Played", out var played)
        && played.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var v))
                continue;
            if (v.ValueKind == JsonValueKind.String)
                return v.GetString();
            if (v.ValueKind == JsonValueKind.Number)
                return v.GetRawText();
        }

        return null;
    }

    private static int? ReadInt(JsonElement el, params string[] names)
    {
        var s = ReadString(el, names);
        return int.TryParse(s, out var n) ? n : null;
    }

    private static long? ReadLong(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var v))
                continue;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
                return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static string? ReadProviderString(JsonElement providers, params string[] names)
    {
        foreach (var name in names)
        {
            if (!providers.TryGetProperty(name, out var value))
                continue;
            var s = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }

        return null;
    }

    private static int? ReadProviderInt(JsonElement providers, params string[] names)
    {
        var raw = ReadProviderString(providers, names);
        return int.TryParse(raw, out var n) && n > 0 ? n : null;
    }
}
