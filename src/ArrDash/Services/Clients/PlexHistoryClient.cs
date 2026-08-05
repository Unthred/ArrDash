using System.Globalization;
using System.Xml.Linq;
using ArrDash.Configuration;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Services.Clients;

/// <summary>
/// Plex-first history ingest via PMS <c>/status/sessions/history/all</c> (no Tautulli required).
/// </summary>
public sealed class PlexHistoryClient(HttpClient http, MediaServiceOptionsAccessor options, ILogger<PlexHistoryClient> logger)
{
    /// <summary>
    /// Modern Plex stores imdb/tmdb/tvdb on <c>Guid</c> children; <c>/library/all?guid=imdb://…</c>
    /// only matches the primary <c>plex://</c> guid. Index provider guids once per TTL instead.
    /// </summary>
    private static readonly TimeSpan GuidIndexTtl = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _guidIndexLock = new(1, 1);
    private Dictionary<string, string>? _guidToRatingKey;
    private DateTimeOffset _guidIndexBuiltAt;

    private PlexOptions Plex => options.Options.Plex;

    public bool IsConfigured => Plex.IsConfigured;

    public async Task<IReadOnlyList<ImportedPlayEvent>> FetchHistoryAsync(
        DateTimeOffset sinceUtc,
        int maxRows,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        var libraryNames = await GetLibraryNameMapAsync(ct);
        var results = new List<ImportedPlayEvent>();
        var start = 0;
        const int pageSize = 100;
        var viewedAtMin = ((DateTimeOffset)sinceUtc).ToUnixTimeSeconds();

        while (results.Count < maxRows)
        {
            var length = Math.Min(pageSize, maxRows - results.Count);
            var url = $"{Plex.Url.TrimEnd('/')}/status/sessions/history/all"
                + $"?X-Plex-Token={Uri.EscapeDataString(Plex.Token)}"
                + $"&viewedAt>={viewedAtMin}"
                + $"&sort=viewedAt:desc";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Plex-Container-Start", start.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Plex-Container-Size", length.ToString(CultureInfo.InvariantCulture));

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                break;

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var videos = doc.Descendants("Video").Concat(doc.Descendants("Track")).ToList();
            if (videos.Count == 0)
                break;

            var stop = false;
            foreach (var el in videos)
            {
                var mapped = MapHistoryElement(el, libraryNames);
                if (mapped is null)
                    continue;

                if (mapped.PlayedAtUtc < sinceUtc)
                {
                    stop = true;
                    break;
                }

                results.Add(mapped);
                if (results.Count >= maxRows)
                    break;
            }

            if (stop || videos.Count < length)
                break;

            start += length;
        }

        return results;
    }

    private static ImportedPlayEvent? MapHistoryElement(
        XElement el,
        IReadOnlyDictionary<string, string> libraryNames)
    {
        var historyKey = el.Attribute("historyKey")?.Value
            ?? el.Attribute("ratingKey")?.Value
            ?? el.Attribute("key")?.Value;
        if (string.IsNullOrWhiteSpace(historyKey))
            return null;

        var viewedAt = ParseUnix(el.Attribute("viewedAt")?.Value);
        if (viewedAt is null)
            return null;

        var mediaType = NormalizeMediaType(el.Attribute("type")?.Value);
        var grandparent = el.Attribute("grandparentTitle")?.Value;
        var itemTitle = el.Attribute("title")?.Value ?? grandparent ?? "Unknown";
        var title = mediaType == "episode" && !string.IsNullOrWhiteSpace(grandparent) ? grandparent : itemTitle;

        var durationMs = ParseLong(el.Attribute("duration")?.Value);
        var durationSec = durationMs > 0 ? (int)(durationMs / 1000) : 0;

        var accountId = el.Attribute("accountID")?.Value;
        var user = el.Descendants("User").FirstOrDefault()?.Attribute("title")?.Value
            ?? el.Attribute("user")?.Value
            ?? "Unknown";

        var thumb = el.Attribute("grandparentThumb")?.Value
            ?? el.Attribute("thumb")?.Value
            ?? el.Attribute("parentThumb")?.Value;

        var ratingKey = el.Attribute("ratingKey")?.Value;
        var grandparentKey = el.Attribute("grandparentRatingKey")?.Value
            ?? el.Attribute("grandparentKey")?.Value;

        var librarySectionId = el.Attribute("librarySectionID")?.Value
            ?? el.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Directory")?.Attribute("key")?.Value;

        string? libraryName = null;
        if (!string.IsNullOrWhiteSpace(librarySectionId)
            && libraryNames.TryGetValue(librarySectionId, out var mappedName))
            libraryName = mappedName;

        double? progress = null;
        var viewOffset = ParseLong(el.Attribute("viewOffset")?.Value);
        if (durationMs > 0 && viewOffset > 0)
            progress = Math.Clamp(viewOffset * 100.0 / durationMs, 0, 100);

        return new ImportedPlayEvent(
            WatchStatsSources.Plex,
            $"plex:{historyKey}",
            user,
            accountId,
            title,
            mediaType == "episode" ? grandparent : null,
            mediaType,
            [],
            el.Attribute("player")?.Value,
            el.Attribute("platform")?.Value,
            viewedAt.Value,
            durationSec,
            ratingKey,
            thumb,
            mediaType == "episode" ? itemTitle : null,
            TranscodeDecision: null,
            LibraryName: libraryName,
            LibraryExternalId: librarySectionId,
            ProgressPercent: progress,
            GrandparentExternalId: grandparentKey,
            SeasonNumber: mediaType == "episode" ? ParseIntAttr(el.Attribute("parentIndex")?.Value) : null,
            EpisodeNumber: mediaType == "episode" ? ParseIntAttr(el.Attribute("index")?.Value) : null);
    }

    private static int? ParseIntAttr(string? value) =>
        int.TryParse(value, out var n) ? n : null;

    private static string NormalizeMediaType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "episode" => "episode",
        "movie" => "movie",
        "track" or "music" => "music",
        _ => raw?.ToLowerInvariant() ?? "other"
    };

    private static DateTimeOffset? ParseUnix(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    public async Task<IReadOnlyList<WatchStatsLibraryInfo>> FetchLibrariesAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        try
        {
            var url = $"{Plex.Url.TrimEnd('/')}/library/sections?X-Plex-Token={Uri.EscapeDataString(Plex.Token)}";
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            return doc.Descendants("Directory")
                .Select(d =>
                {
                    var id = d.Attribute("key")?.Value;
                    var title = d.Attribute("title")?.Value;
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                        return null;
                    var type = d.Attribute("type")?.Value;
                    return new WatchStatsLibraryInfo(WatchStatsSources.Plex, id, title, type);
                })
                .Where(l => l is not null)
                .Cast<WatchStatsLibraryInfo>()
                .ToList();
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "FetchLibrariesAsync failed");
            return [];
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetLibraryNameMapAsync(CancellationToken ct)
    {
        var libs = await FetchLibrariesAsync(ct);
        return libs.ToDictionary(l => l.ExternalId, l => l.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Find a Plex ratingKey by provider guid (tmdb:// / imdb:// / tvdb://).</summary>
    public async Task<string?> FindRatingKeyByProviderAsync(
        string? imdbId,
        int? tmdbId,
        int? tvdbId,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var index = await GetProviderGuidIndexAsync(ct);
        if (index.Count == 0)
            return null;

        foreach (var guid in BuildGuids(imdbId, tmdbId, tvdbId))
        {
            if (index.TryGetValue(guid, out var ratingKey) && !string.IsNullOrWhiteSpace(ratingKey))
                return ratingKey;
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetProviderGuidIndexAsync(CancellationToken ct)
    {
        if (_guidToRatingKey is { Count: > 0 } cached
            && DateTimeOffset.UtcNow - _guidIndexBuiltAt < GuidIndexTtl)
            return cached;

        await _guidIndexLock.WaitAsync(ct);
        try
        {
            if (_guidToRatingKey is { Count: > 0 } again
                && DateTimeOffset.UtcNow - _guidIndexBuiltAt < GuidIndexTtl)
                return again;

            var map = await BuildProviderGuidIndexAsync(ct);
            _guidToRatingKey = map;
            _guidIndexBuiltAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Built Plex provider guid index with {Count:N0} entries", map.Count);
            return map;
        }
        finally
        {
            _guidIndexLock.Release();
        }
    }

    private async Task<Dictionary<string, string>> BuildProviderGuidIndexAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var libs = await FetchLibrariesAsync(ct);
        foreach (var lib in libs)
        {
            ct.ThrowIfCancellationRequested();
            if (string.Equals(lib.MediaType, "movie", StringComparison.OrdinalIgnoreCase))
                await IndexSectionAsync(lib.ExternalId, mediaType: null, map, ct);
            else if (string.Equals(lib.MediaType, "show", StringComparison.OrdinalIgnoreCase))
                await IndexSectionAsync(lib.ExternalId, mediaType: "4", map, ct); // 4 = episode
        }

        return map;
    }

    private async Task IndexSectionAsync(
        string sectionKey,
        string? mediaType,
        Dictionary<string, string> map,
        CancellationToken ct)
    {
        const int pageSize = 500;
        var start = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{Plex.Url.TrimEnd('/')}/library/sections/{Uri.EscapeDataString(sectionKey)}/all"
                + $"?includeGuids=1"
                + $"&X-Plex-Container-Start={start.ToString(CultureInfo.InvariantCulture)}"
                + $"&X-Plex-Container-Size={pageSize.ToString(CultureInfo.InvariantCulture)}"
                + $"&X-Plex-Token={Uri.EscapeDataString(Plex.Token)}";
            if (!string.IsNullOrWhiteSpace(mediaType))
                url += $"&type={Uri.EscapeDataString(mediaType)}";

            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                break;

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var container = doc.Root;
            var totalSize = int.TryParse(container?.Attribute("totalSize")?.Value, out var total) ? total : 0;
            var videos = doc.Descendants("Video").ToList();
            if (videos.Count == 0)
                break;

            foreach (var video in videos)
            {
                var ratingKey = video.Attribute("ratingKey")?.Value;
                if (string.IsNullOrWhiteSpace(ratingKey))
                    continue;

                foreach (var guidEl in video.Elements("Guid"))
                {
                    var guid = guidEl.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(guid))
                        continue;
                    // First wins — avoid overwriting with duplicate library copies.
                    map.TryAdd(guid, ratingKey);
                }
            }

            start += pageSize;
            if (totalSize > 0 && start >= totalSize)
                break;
            if (videos.Count < pageSize)
                break;
        }
    }

    /// <summary>
    /// Marks a Plex item watched using the configured server token (token owner's account).
    /// Additive only.
    /// </summary>
    public async Task<bool> MarkWatchedAsync(string ratingKey, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(ratingKey))
            return false;

        try
        {
            var url = $"{Plex.Url.TrimEnd('/')}/:/scrobble"
                + $"?identifier=com.plexapp.plugins.library"
                + $"&key={Uri.EscapeDataString(ratingKey)}"
                + $"&X-Plex-Token={Uri.EscapeDataString(Plex.Token)}";
            using var response = await http.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MarkWatchedAsync failed for {RatingKey}", ratingKey);
            return false;
        }
    }

    /// <summary>
    /// Pages movie/show libraries for items with viewCount &gt; 0 (token owner = Squiggley).
    /// Includes missing-on-disk library rows when Plex still reports them.
    /// </summary>
    public async Task<IReadOnlyList<LibraryWatchedItem>> FetchWatchedLibraryItemsAsync(
        IReadOnlyList<string>? excludedLibraryKeys,
        CancellationToken ct,
        int maxItems = 10_000)
    {
        var results = new List<LibraryWatchedItem>();
        if (!IsConfigured)
            return results;

        var libs = await FetchLibrariesAsync(ct);
        foreach (var lib in libs)
        {
            ct.ThrowIfCancellationRequested();
            if (WatchStatsLibraryFilter.IsExcluded(excludedLibraryKeys, WatchStatsSources.Plex, lib.ExternalId))
                continue;

            if (string.Equals(lib.MediaType, "movie", StringComparison.OrdinalIgnoreCase))
                await CollectWatchedSectionAsync(lib.ExternalId, mediaTypeFilter: null, itemMediaType: "movie", results, maxItems, ct);
            else if (string.Equals(lib.MediaType, "show", StringComparison.OrdinalIgnoreCase))
                await CollectWatchedSectionAsync(lib.ExternalId, mediaTypeFilter: "4", itemMediaType: "episode", results, maxItems, ct);

            if (results.Count >= maxItems)
                break;
        }

        return results;
    }

    private async Task CollectWatchedSectionAsync(
        string sectionKey,
        string? mediaTypeFilter,
        string itemMediaType,
        List<LibraryWatchedItem> results,
        int maxItems,
        CancellationToken ct)
    {
        const int pageSize = 500;
        var start = 0;

        while (results.Count < maxItems)
        {
            ct.ThrowIfCancellationRequested();
            var url = $"{Plex.Url.TrimEnd('/')}/library/sections/{Uri.EscapeDataString(sectionKey)}/all"
                + $"?includeGuids=1"
                + $"&X-Plex-Container-Start={start.ToString(CultureInfo.InvariantCulture)}"
                + $"&X-Plex-Container-Size={pageSize.ToString(CultureInfo.InvariantCulture)}"
                + $"&X-Plex-Token={Uri.EscapeDataString(Plex.Token)}";
            if (!string.IsNullOrWhiteSpace(mediaTypeFilter))
                url += $"&type={Uri.EscapeDataString(mediaTypeFilter)}";

            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                break;

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var container = doc.Root;
            var totalSize = int.TryParse(container?.Attribute("totalSize")?.Value, out var total) ? total : 0;
            var videos = doc.Descendants("Video").ToList();
            if (videos.Count == 0)
                break;

            foreach (var video in videos)
            {
                var viewCount = int.TryParse(video.Attribute("viewCount")?.Value, out var vc) ? vc : 0;
                if (viewCount <= 0)
                    continue;

                var ratingKey = video.Attribute("ratingKey")?.Value;
                if (string.IsNullOrWhiteSpace(ratingKey))
                    continue;

                string? imdb = null;
                int? tmdb = null;
                int? tvdb = null;
                foreach (var guidEl in video.Elements("Guid"))
                {
                    var guid = guidEl.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(guid))
                        continue;
                    if (guid.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
                        imdb ??= guid["imdb://".Length..];
                    else if (guid.StartsWith("tmdb://", StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(guid["tmdb://".Length..], out var tmdbId))
                        tmdb ??= tmdbId;
                    else if (guid.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase)
                             && int.TryParse(guid["tvdb://".Length..], out var tvdbId))
                        tvdb ??= tvdbId;
                }

                DateTimeOffset? watchedAt = null;
                if (long.TryParse(video.Attribute("lastViewedAt")?.Value, out var unix)
                    && unix > 0)
                    watchedAt = DateTimeOffset.FromUnixTimeSeconds(unix);

                var title = video.Attribute("title")?.Value ?? "Unknown";
                var series = video.Attribute("grandparentTitle")?.Value;
                int? season = int.TryParse(video.Attribute("parentIndex")?.Value, out var sn) ? sn : null;
                int? episode = int.TryParse(video.Attribute("index")?.Value, out var en) ? en : null;
                int? year = int.TryParse(video.Attribute("year")?.Value, out var y) ? y : null;

                results.Add(new LibraryWatchedItem(
                    WatchStatsSources.Plex,
                    "token-owner",
                    ratingKey,
                    itemMediaType,
                    itemMediaType == "episode" && !string.IsNullOrWhiteSpace(series) ? series : title,
                    series,
                    imdb,
                    tmdb,
                    tvdb,
                    null,
                    year,
                    season,
                    episode,
                    watchedAt,
                    sectionKey));

                if (results.Count >= maxItems)
                    return;
            }

            start += pageSize;
            if (totalSize > 0 && start >= totalSize)
                break;
            if (videos.Count < pageSize)
                break;
        }
    }

    private static IEnumerable<string> BuildGuids(string? imdbId, int? tmdbId, int? tvdbId)
    {
        if (!string.IsNullOrWhiteSpace(imdbId))
            yield return $"imdb://{imdbId.Trim()}";
        if (tmdbId is int tmdb and > 0)
            yield return $"tmdb://{tmdb}";
        if (tvdbId is int tvdb and > 0)
            yield return $"tvdb://{tvdb}";
    }
}
