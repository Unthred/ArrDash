using System.Text.Json;
using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services.Clients;

public class PlaybackReportingClient(HttpClient http, ServiceEndpoint endpoint, string sourceKey, string sourceLabel, ILogger logger)
{
    public string SourceKey => sourceKey;
    public string SourceLabel => sourceLabel;
    public bool IsConfigured => endpoint.IsConfigured;

    public async Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, $"{sourceLabel} URL or API key required");

        try
        {
            var url = BuildUrl("/System/Info/Public");
            using var response = await http.GetAsync(url, ct);
            return response.IsSuccessStatusCode
                ? (true, "Connected")
                : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TestConnectionAsync failed");
            return (false, ex.Message);
        }
    }

    public async Task<(bool PluginAvailable, string? Error)> TestPluginAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, "Not configured");

        try
        {
            var url = BuildUrl("/user_usage_stats/user_list");
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, "Playback Reporting plugin required");
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}");
            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TestPluginAsync failed");
            return (false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<ImportedPlayEvent>> FetchHistoryAsync(DateTimeOffset sinceUtc, CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        var users = await FetchUsersAsync(ct);
        if (users.Count == 0)
            return [];

        var startDate = sinceUtc.UtcDateTime.ToString("yyyy-MM-dd");
        var endDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var all = new List<ImportedPlayEvent>();

        foreach (var user in users)
        {
            var url = BuildUrl("/user_usage_stats/UserPlaylist",
                ("user_id", user.Id),
                ("start_date", startDate),
                ("end_date", endDate));

            try
            {
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                all.AddRange(ParsePlaylist(doc.RootElement, user));
            }
            catch (Exception ex) {
                logger.LogWarning(ex, "FetchHistoryAsync failed");
                // Continue with other users.
            }
        }

        return all;
    }

    public async Task<WatchStatsSourceSnapshot?> BuildLiveSnapshotAsync(
        WatchStatsRange range,
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var since = WatchStatsPeriodHelper.CutoffUtc(range);
            var events = await FetchHistoryAsync(new DateTimeOffset(since, TimeSpan.Zero), ct);
            if (events.Count == 0)
                return null;

            var summary = new WatchStatsSummary(
                Math.Round(events.Sum(e => e.DurationSeconds) / 3600.0, 1),
                events.Count,
                events.Select(e => e.UserDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                events.Max(e => e.PlayedAtUtc));

            return new WatchStatsSourceSnapshot(
                sourceKey,
                sourceLabel,
                true,
                null,
                summary,
                BuildTopUsers(events, limit),
                BuildTopGenres(events, limit),
                BuildTopMedia(events, "movie", limit),
                BuildTopMedia(events, "episode", limit),
                BuildTopMedia(events, "music", limit),
                BuildTopPlatforms(events, limit),
                BuildRecent(events, limit));
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "BuildLiveSnapshotAsync failed");
            return null;
        }
    }

    private static IReadOnlyList<WatchStatRow> BuildTopUsers(IReadOnlyList<ImportedPlayEvent> events, int limit) =>
        events.GroupBy(e => e.UserDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                null,
                null,
                g.First().Source,
                DrilldownKey: g.Select(e => e.UserExternalId).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? g.Key,
                DrilldownKind: ActivityDrilldownKind.User))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopGenres(IReadOnlyList<ImportedPlayEvent> events, int limit)
    {
        var genreHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genrePlays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var ev in events)
        {
            foreach (var genre in ev.Genres)
            {
                genreHours[genre] = genreHours.GetValueOrDefault(genre) + ev.DurationSeconds / 3600.0;
                genrePlays[genre] = genrePlays.GetValueOrDefault(genre) + 1;
            }
        }

        return genreHours
            .Select(kv => new WatchStatRow(
                kv.Key,
                $"{genrePlays[kv.Key]} plays",
                Math.Round(kv.Value, 1),
                genrePlays[kv.Key],
                null,
                null,
                null,
                DrilldownKey: kv.Key,
                DrilldownKind: ActivityDrilldownKind.Genre))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> BuildTopMedia(IReadOnlyList<ImportedPlayEvent> events, string mediaType, int limit) =>
        events.Where(e => string.Equals(e.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                mediaType == "episode" ? g.Select(e => e.SeriesTitle).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) : null,
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                BuildThumb(g.First()),
                null,
                g.First().Source,
                DrilldownKey: g.Key,
                DrilldownKind: ActivityDrilldownKind.Media))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopPlatforms(IReadOnlyList<ImportedPlayEvent> events, int limit) =>
        events.GroupBy(e => e.Platform ?? e.Client ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                null,
                null,
                g.First().Source,
                DrilldownKey: g.Key,
                DrilldownKind: ActivityDrilldownKind.Platform))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildRecent(IReadOnlyList<ImportedPlayEvent> events, int limit) =>
        events
            .OrderByDescending(e => e.PlayedAtUtc)
            .GroupBy(RecentGroupKey)
            .Select(g => g.First())
            .Take(limit)
            .Select(e => new WatchStatRow(
                e.Title,
                $"{e.UserDisplayName} · {WatchStatsSources.Label(e.Source)}",
                Math.Round(e.DurationSeconds / 3600.0, 1),
                1,
                BuildThumb(e),
                null,
                e.Source,
                DrilldownKey: e.Title,
                DrilldownKind: ActivityDrilldownKind.Media))
            .ToList();

    // Groups repeat watches of the same episode/movie into a single "recently watched" row,
    // keeping just the most recent play (events are pre-sorted newest-first before grouping).
    private static string RecentGroupKey(ImportedPlayEvent e) =>
        !string.IsNullOrWhiteSpace(e.ExternalItemId)
            ? $"id:{e.Source}:{e.ExternalItemId}"
            : $"t:{e.Source}:{e.Title}";

    private static string? BuildThumb(ImportedPlayEvent e)
    {
        if (string.IsNullOrWhiteSpace(e.ExternalItemId))
            return null;

        return e.Source switch
        {
            WatchStatsSources.Emby => PosterUrls.EmbyItem(e.ExternalItemId),
            WatchStatsSources.Jellyfin => PosterUrls.JellyfinItem(e.ExternalItemId),
            _ => null
        };
    }

    private async Task<IReadOnlyList<PlaybackUser>> FetchUsersAsync(CancellationToken ct)
    {
        var url = BuildUrl("/user_usage_stats/user_list");
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return [];

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
            return ParseUsers(root);

        if (root.TryGetProperty("Users", out var users) && users.ValueKind == JsonValueKind.Array)
            return ParseUsers(users);

        return [];
    }

    private static IReadOnlyList<PlaybackUser> ParseUsers(JsonElement array) =>
        array.EnumerateArray()
            .Select(u => new PlaybackUser(
                ReadString(u, "Id", "id", "user_id") ?? "",
                ReadString(u, "Name", "name", "user_name") ?? "Unknown"))
            .Where(u => !string.IsNullOrWhiteSpace(u.Id))
            .ToList();

    private IEnumerable<ImportedPlayEvent> ParsePlaylist(JsonElement root, PlaybackUser user)
    {
        var rows = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("Items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray()
                : root.TryGetProperty("items", out var itemsLower) && itemsLower.ValueKind == JsonValueKind.Array
                    ? itemsLower.EnumerateArray()
                    : Enumerable.Empty<JsonElement>();

        foreach (var row in rows)
        {
            var playedAt = ReadDate(row, "DatePlayed", "date_played", "PlayedAt", "played_at") ?? DateTimeOffset.UtcNow;
            var playId = ReadString(row, "Id", "id", "ItemId", "item_id") ?? Guid.NewGuid().ToString("N");
            var mediaType = NormalizeMediaType(ReadString(row, "Type", "type", "MediaType", "media_type"));
            var title = ReadString(row, "Name", "name", "Title", "title") ?? "Unknown";
            var series = ReadString(row, "SeriesName", "series_name", "ParentName", "parent_name");
            var ticks = ReadLong(row, "RunTimeTicks", "run_time_ticks");
            var duration = ticks is > 0
                ? (int)Math.Min(ticks.Value / 10_000_000, int.MaxValue)
                : ReadInt(row, "Duration", "duration", "PlayDuration", "play_duration") ?? 0;

            yield return new ImportedPlayEvent(
                sourceKey,
                $"{user.Id}:{playId}:{playedAt.ToUnixTimeSeconds()}",
                user.Name,
                user.Id,
                mediaType == "episode" && !string.IsNullOrWhiteSpace(series) ? series : title,
                mediaType == "episode" ? series : null,
                mediaType,
                ParseGenres(row),
                ReadString(row, "Client", "client", "DeviceName", "device_name"),
                ReadString(row, "Platform", "platform"),
                playedAt,
                Math.Max(duration, 0),
                ReadString(row, "ItemId", "item_id", "Id", "id"),
                ReadString(row, "PrimaryImageTag", "primary_image_tag"),
                ItemTitle: mediaType == "episode" ? title : null);
        }
    }

    private string BuildUrl(string path, params (string Key, string Value)[] query)
    {
        var parts = new List<string> { $"api_key={Uri.EscapeDataString(endpoint.ApiKey)}" };
        parts.AddRange(query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
        return $"{endpoint.Url.TrimEnd('/')}{path}?{string.Join('&', parts)}";
    }

    public async Task<IReadOnlyList<WatchStatsLibraryInfo>> FetchLibrariesAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        try
        {
            var url = BuildUrl("/Library/VirtualFolders");
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<WatchStatsLibraryInfo>();
            foreach (var folder in doc.RootElement.EnumerateArray())
            {
                var id = ReadString(folder, "ItemId", "Guid", "Name");
                var name = ReadString(folder, "Name", "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;
                var type = ReadString(folder, "CollectionType", "collection_type");
                list.Add(new WatchStatsLibraryInfo(sourceKey, id, name, type));
            }

            return list;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "FetchLibrariesAsync failed");
            return [];
        }
    }

    public sealed record LibraryRoot(string Id, string Name, IReadOnlyList<string> Locations);

    /// <summary>Libraries with their filesystem locations, for mapping item paths to libraries.</summary>
    public async Task<IReadOnlyList<LibraryRoot>> FetchLibraryRootsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        try
        {
            var url = BuildUrl("/Library/VirtualFolders");
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var roots = new List<LibraryRoot>();
            foreach (var folder in doc.RootElement.EnumerateArray())
            {
                // Same id precedence as FetchLibrariesAsync so filter keys stay consistent.
                var id = ReadString(folder, "ItemId", "Guid", "Name");
                var name = ReadString(folder, "Name", "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                var locations = new List<string>();
                if (folder.TryGetProperty("Locations", out var locs) && locs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var loc in locs.EnumerateArray())
                    {
                        var path = loc.ValueKind == JsonValueKind.String ? loc.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(path))
                            locations.Add(path);
                    }
                }

                roots.Add(new LibraryRoot(id, name, locations));
            }

            return roots;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "FetchLibraryRootsAsync failed");
            return [];
        }
    }

    /// <summary>Filesystem paths for a batch of item ids; items the server no longer knows are omitted.</summary>
    public async Task<IReadOnlyDictionary<string, string>> FetchItemPathsAsync(
        IReadOnlyList<string> itemIds,
        CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!IsConfigured || itemIds.Count == 0)
            return map;

        try
        {
            var url = BuildUrl("/Items",
                ("Ids", string.Join(',', itemIds)),
                ("Fields", "Path"));
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return map;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                return map;

            foreach (var item in items.EnumerateArray())
            {
                var id = ReadString(item, "Id");
                var path = ReadString(item, "Path");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(path))
                    map[id] = path;
            }

            return map;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "FetchItemPathsAsync failed");
            return map;
        }
    }

    /// <summary>
    /// Episode list for a series id (used when Tracearr stored the series thumb id but we have an episode title).
    /// </summary>
    public async Task<IReadOnlyList<MediaSeriesEpisode>> FetchSeriesEpisodesAsync(
        string seriesId,
        CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(seriesId))
            return [];

        try
        {
            var url = BuildUrl($"/Shows/{Uri.EscapeDataString(seriesId)}/Episodes",
                ("Fields", "IndexNumber,ParentIndexNumber,ProviderIds"),
                ("EnableUserData", "false"));
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<MediaSeriesEpisode>();
            foreach (var item in items.EnumerateArray())
            {
                var id = ReadString(item, "Id");
                var name = ReadString(item, "Name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;

                list.Add(new MediaSeriesEpisode(
                    id,
                    name,
                    ReadInt(item, "ParentIndexNumber"),
                    ReadInt(item, "IndexNumber")));
            }

            return list;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FetchSeriesEpisodesAsync failed for {SeriesId}", seriesId);
            return [];
        }
    }

    /// <summary>
    /// Provider ids (and optional episode indexes) for a batch of Emby/Jellyfin item ids.
    /// Tracearr history often stores the series id for episodes — callers still get show-level ids.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, MediaItemProviderInfo>> FetchItemProviderInfoAsync(
        IReadOnlyList<string> itemIds,
        CancellationToken ct)
    {
        var map = new Dictionary<string, MediaItemProviderInfo>(StringComparer.OrdinalIgnoreCase);
        if (!IsConfigured || itemIds.Count == 0)
            return map;

        try
        {
            var url = BuildUrl("/Items",
                ("Ids", string.Join(',', itemIds)),
                ("Fields", "ProviderIds,ProductionYear,IndexNumber,ParentIndexNumber,SeriesId,Type"));
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return map;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                return map;

            foreach (var item in items.EnumerateArray())
            {
                var id = ReadString(item, "Id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                string? imdb = null;
                int? tmdb = null;
                int? tvdb = null;
                int? trakt = null;
                if (item.TryGetProperty("ProviderIds", out var providers) && providers.ValueKind == JsonValueKind.Object)
                {
                    imdb = ReadProviderString(providers, "Imdb", "IMDB", "imdb");
                    tmdb = ReadProviderInt(providers, "Tmdb", "TMDb", "tmdb");
                    tvdb = ReadProviderInt(providers, "Tvdb", "TVDB", "tvdb");
                    trakt = ReadProviderInt(providers, "Trakt", "trakt");
                }

                map[id] = new MediaItemProviderInfo(
                    id,
                    ReadString(item, "Type"),
                    imdb,
                    tmdb,
                    tvdb,
                    trakt,
                    ReadInt(item, "ProductionYear"),
                    ReadInt(item, "ParentIndexNumber"),
                    ReadInt(item, "IndexNumber"),
                    ReadString(item, "SeriesId"));
            }

            return map;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FetchItemProviderInfoAsync failed");
            return map;
        }
    }

    /// <summary>Emby/Jellyfin users from the core Users API (preferred for mark-watched).</summary>
    public async Task<IReadOnlyList<(string Id, string Name)>> ListUsersAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        try
        {
            var url = BuildUrl("/Users");
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            return doc.RootElement.EnumerateArray()
                .Select(u => (
                    Id: ReadString(u, "Id") ?? "",
                    Name: ReadString(u, "Name") ?? ""))
                .Where(u => !string.IsNullOrWhiteSpace(u.Id) && !string.IsNullOrWhiteSpace(u.Name))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ListUsersAsync failed");
            return [];
        }
    }

    /// <summary>Find a library item by provider id (Imdb / Tmdb / Tvdb / Trakt).</summary>
    public async Task<string?> FindItemIdByProviderAsync(
        string mediaType,
        string? imdbId,
        int? tmdbId,
        int? tvdbId,
        int? traktId,
        int? seasonNumber,
        int? episodeNumber,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var includeType = string.Equals(mediaType, "episode", StringComparison.OrdinalIgnoreCase)
            ? "Episode"
            : "Movie";

        foreach (var providerKey in BuildProviderKeys(imdbId, tmdbId, tvdbId, traktId))
        {
            try
            {
                var url = BuildUrl("/Items",
                    ("Recursive", "true"),
                    ("IncludeItemTypes", includeType),
                    ("AnyProviderIdEquals", providerKey),
                    ("Fields", "ProviderIds,ParentIndexNumber,IndexNumber"),
                    ("Limit", "25"));
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    continue;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in items.EnumerateArray())
                {
                    var id = ReadString(item, "Id");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    if (string.Equals(includeType, "Episode", StringComparison.OrdinalIgnoreCase)
                        && seasonNumber is int sn
                        && episodeNumber is int en)
                    {
                        var itemSeason = ReadInt(item, "ParentIndexNumber");
                        var itemEpisode = ReadInt(item, "IndexNumber");
                        if (itemSeason != sn || itemEpisode != en)
                            continue;
                    }

                    return id;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "FindItemIdByProviderAsync failed for {Provider}", providerKey);
            }
        }

        if (string.Equals(mediaType, "episode", StringComparison.OrdinalIgnoreCase)
            && seasonNumber is int season
            && episodeNumber is int episode)
        {
            foreach (var providerKey in BuildProviderKeys(imdbId, tmdbId, tvdbId, traktId))
            {
                try
                {
                    var seriesUrl = BuildUrl("/Items",
                        ("Recursive", "true"),
                        ("IncludeItemTypes", "Series"),
                        ("AnyProviderIdEquals", providerKey),
                        ("Limit", "5"));
                    using var seriesResponse = await http.GetAsync(seriesUrl, ct);
                    if (!seriesResponse.IsSuccessStatusCode)
                        continue;

                    await using var seriesStream = await seriesResponse.Content.ReadAsStreamAsync(ct);
                    using var seriesDoc = await JsonDocument.ParseAsync(seriesStream, cancellationToken: ct);
                    if (!seriesDoc.RootElement.TryGetProperty("Items", out var seriesItems)
                        || seriesItems.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var series in seriesItems.EnumerateArray())
                    {
                        var seriesId = ReadString(series, "Id");
                        if (string.IsNullOrWhiteSpace(seriesId))
                            continue;

                        var episodes = await FetchSeriesEpisodesAsync(seriesId, ct);
                        var match = episodes.FirstOrDefault(e => e.SeasonNumber == season && e.EpisodeNumber == episode);
                        if (match is not null)
                            return match.ItemId;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Series episode resolve failed for {Provider}", providerKey);
                }
            }
        }

        return null;
    }

    /// <summary>Marks an item played for a user. Additive only — never unmarks.</summary>
    public async Task<bool> MarkPlayedAsync(string userId, string itemId, DateTimeOffset? playedAt, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(itemId))
            return false;

        try
        {
            var query = new List<(string Key, string Value)>();
            if (playedAt is { } at)
                query.Add(("DatePlayed", at.UtcDateTime.ToString("yyyyMMddHHmmss")));

            var url = BuildUrl($"/Users/{Uri.EscapeDataString(userId)}/PlayedItems/{Uri.EscapeDataString(itemId)}",
                query.ToArray());
            using var response = await http.PostAsync(url, content: null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MarkPlayedAsync failed for user {UserId} item {ItemId}", userId, itemId);
            return false;
        }
    }

    public async Task<bool> IsPlayedAsync(string userId, string itemId, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(itemId))
            return false;

        try
        {
            var url = BuildUrl($"/Users/{Uri.EscapeDataString(userId)}/Items/{Uri.EscapeDataString(itemId)}",
                ("Fields", "UserData"));
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("UserData", out var userData))
                return false;
            return userData.TryGetProperty("Played", out var played)
                   && played.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "IsPlayedAsync failed for {ItemId}", itemId);
            return false;
        }
    }

    /// <summary>
    /// Pages Emby/Jellyfin library items marked Played for a user (movies + episodes),
    /// including items with missing files. Optionally scoped to a library ParentId.
    /// </summary>
    public async Task<IReadOnlyList<LibraryWatchedItem>> FetchPlayedItemsAsync(
        string userId,
        string? libraryParentId,
        CancellationToken ct,
        int maxItems = 10_000)
    {
        var results = new List<LibraryWatchedItem>();
        if (!IsConfigured || string.IsNullOrWhiteSpace(userId))
            return results;

        const int pageSize = 200;
        var start = 0;

        while (results.Count < maxItems)
        {
            ct.ThrowIfCancellationRequested();
            var query = new List<(string Key, string Value)>
            {
                ("Recursive", "true"),
                ("IsPlayed", "true"),
                ("IncludeItemTypes", "Movie,Episode"),
                ("Fields", "ProviderIds,UserData,Path,ProductionYear,ParentId,SeriesId,SeriesName,IndexNumber,ParentIndexNumber"),
                ("StartIndex", start.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Limit", pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };
            if (!string.IsNullOrWhiteSpace(libraryParentId))
                query.Add(("ParentId", libraryParentId));

            try
            {
                var url = BuildUrl($"/Users/{Uri.EscapeDataString(userId)}/Items", query.ToArray());
                using var response = await http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    break;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!doc.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                    break;

                var batchCount = 0;
                foreach (var item in items.EnumerateArray())
                {
                    batchCount++;
                    var parsed = ParseLibraryWatchedItem(item, userId, libraryParentId);
                    if (parsed is not null)
                        results.Add(parsed);
                    if (results.Count >= maxItems)
                        break;
                }

                var total = doc.RootElement.TryGetProperty("TotalRecordCount", out var totalEl)
                            && totalEl.TryGetInt32(out var t)
                    ? t
                    : 0;
                start += pageSize;
                if (batchCount == 0)
                    break;
                if (total > 0 && start >= total)
                    break;
                if (batchCount < pageSize)
                    break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FetchPlayedItemsAsync failed for user {UserId} library {Library}", userId, libraryParentId);
                break;
            }
        }

        return results;
    }

    private LibraryWatchedItem? ParseLibraryWatchedItem(JsonElement item, string userId, string? libraryParentId)
    {
        var id = ReadString(item, "Id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var typeRaw = ReadString(item, "Type") ?? "";
        var mediaType = typeRaw.Equals("Episode", StringComparison.OrdinalIgnoreCase) ? "episode"
            : typeRaw.Equals("Movie", StringComparison.OrdinalIgnoreCase) ? "movie"
            : null;
        if (mediaType is null)
            return null;

        string? imdb = null;
        int? tmdb = null;
        int? tvdb = null;
        int? trakt = null;
        if (item.TryGetProperty("ProviderIds", out var providers) && providers.ValueKind == JsonValueKind.Object)
        {
            imdb = ReadProviderString(providers, "Imdb", "IMDB", "imdb");
            tmdb = ReadProviderInt(providers, "Tmdb", "TMDb", "tmdb");
            tvdb = ReadProviderInt(providers, "Tvdb", "TVDB", "tvdb");
            trakt = ReadProviderInt(providers, "Trakt", "trakt");
        }

        DateTimeOffset? watchedAt = null;
        if (item.TryGetProperty("UserData", out var userData) && userData.ValueKind == JsonValueKind.Object
            && userData.TryGetProperty("LastPlayedDate", out var lastPlayed)
            && lastPlayed.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(lastPlayed.GetString(), out var parsedAt))
        {
            watchedAt = parsedAt;
        }

        var title = ReadString(item, "Name") ?? "Unknown";
        var series = ReadString(item, "SeriesName");
        return new LibraryWatchedItem(
            sourceKey,
            userId,
            id,
            mediaType,
            mediaType == "episode" && !string.IsNullOrWhiteSpace(series) ? series : title,
            series,
            imdb,
            tmdb,
            tvdb,
            trakt,
            ReadInt(item, "ProductionYear"),
            ReadInt(item, "ParentIndexNumber"),
            ReadInt(item, "IndexNumber"),
            watchedAt,
            libraryParentId);
    }

    /// <summary>
    /// Emby/Jellyfin <c>AnyProviderIdEquals</c> expects <c>Tmdb.123</c> / <c>Imdb.tt…</c>
    /// (dot separator). Equals-sign forms return zero hits.
    /// </summary>
    private static IEnumerable<string> BuildProviderKeys(string? imdbId, int? tmdbId, int? tvdbId, int? traktId)
    {
        if (!string.IsNullOrWhiteSpace(imdbId))
            yield return $"Imdb.{imdbId.Trim()}";
        if (tmdbId is int tmdb and > 0)
            yield return $"Tmdb.{tmdb}";
        if (tvdbId is int tvdb and > 0)
            yield return $"Tvdb.{tvdb}";
        if (traktId is int trakt and > 0)
            yield return $"Trakt.{trakt}";
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

    private static string? ReadString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static long? ReadLong(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var l))
                return l;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static int? ReadInt(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out var n))
                    return n;
                if (value.TryGetInt64(out var l))
                    return (int)Math.Min(l, int.MaxValue);
            }
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var parsed))
                return parsed.ToUniversalTime();

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
                return DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        return null;
    }

    private static IReadOnlyList<string> ParseGenres(JsonElement row)
    {
        if (row.TryGetProperty("Genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            return genres.EnumerateArray().Select(g => g.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToList();
        if (row.TryGetProperty("genres", out var genresLower) && genresLower.ValueKind == JsonValueKind.Array)
            return genresLower.EnumerateArray().Select(g => g.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToList();

        var text = ReadString(row, "Genres", "genres");
        return string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string NormalizeMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "movie" => "movie",
        "episode" => "episode",
        "audio" => "music",
        "music" => "music",
        _ => "other"
    };

    private sealed record PlaybackUser(string Id, string Name);
}

public sealed class EmbyPlaybackReportingClient(HttpClient http, MediaServiceOptionsAccessor options, ILogger<EmbyPlaybackReportingClient> logger)
    : PlaybackReportingClient(http, options.Options.Emby, WatchStatsSources.Emby, "Emby", logger);

public sealed class JellyfinPlaybackReportingClient(HttpClient http, MediaServiceOptionsAccessor options, ILogger<JellyfinPlaybackReportingClient> logger)
    : PlaybackReportingClient(http, options.Options.Jellyfin, WatchStatsSources.Jellyfin, "Jellyfin", logger);

public sealed record MediaItemProviderInfo(
    string ItemId,
    string? Type,
    string? ImdbId,
    int? TmdbId,
    int? TvdbId,
    int? TraktId,
    int? Year,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? SeriesId);

public sealed record MediaSeriesEpisode(
    string ItemId,
    string Name,
    int? SeasonNumber,
    int? EpisodeNumber);

/// <summary>Library item marked watched on Emby/Jellyfin/Plex (may have no file on disk).</summary>
public sealed record LibraryWatchedItem(
    string Source,
    string ServerUserId,
    string ServerItemId,
    string MediaType,
    string Title,
    string? SeriesTitle,
    string? ImdbId,
    int? TmdbId,
    int? TvdbId,
    int? TraktId,
    int? Year,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateTimeOffset? WatchedAt,
    string? LibraryExternalId);

