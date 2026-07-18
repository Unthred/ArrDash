using System.Globalization;
using System.Xml.Linq;
using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services.Clients;

/// <summary>
/// Plex-first history ingest via PMS <c>/status/sessions/history/all</c> (no Tautulli required).
/// </summary>
public sealed class PlexHistoryClient(HttpClient http, MediaServiceOptionsAccessor options)
{
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
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetLibraryNameMapAsync(CancellationToken ct)
    {
        var libs = await FetchLibrariesAsync(ct);
        return libs.ToDictionary(l => l.ExternalId, l => l.Name, StringComparer.OrdinalIgnoreCase);
    }
}
