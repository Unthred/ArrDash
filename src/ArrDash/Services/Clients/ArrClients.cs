using System.Net.Http.Headers;
using System.Text.Json;
using ArrDash.Configuration;
using ArrDash.Models;
using ArrDash.Services;
using Microsoft.Extensions.Options;

namespace ArrDash.Services.Clients;

public abstract class ArrClientBase
{
    protected readonly HttpClient Http;
    private readonly MediaServiceOptionsAccessor _optionsAccessor;
    private readonly Func<MediaServiceOptions, ServiceEndpoint> _selector;
    protected ServiceEndpoint Options => _selector(_optionsAccessor.Options);
    protected readonly string ApiVersion;
    protected readonly MediaSource Source;
    protected readonly MediaType DefaultMediaType;

    protected ArrClientBase(
        HttpClient http,
        MediaServiceOptionsAccessor optionsAccessor,
        Func<MediaServiceOptions, ServiceEndpoint> selector,
        string apiVersion,
        MediaSource source,
        MediaType defaultMediaType)
    {
        Http = http;
        _optionsAccessor = optionsAccessor;
        _selector = selector;
        ApiVersion = apiVersion;
        Source = source;
        DefaultMediaType = defaultMediaType;
    }

    public bool IsConfigured => Options.IsConfigured;

    public virtual async Task<(IReadOnlyList<DownloadItem> Items, ServiceHealth Health)> FetchRecentAsync(
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return ([], new ServiceHealth(Source.ToString().ToLowerInvariant(), Source.ToString(), false, false, null, null));
        }

        try
        {
            var fetchSize = Math.Clamp(limit * 3, limit, 150);
            var url = $"{Options.Url.TrimEnd('/')}/api/{ApiVersion}/history?pageSize={fetchSize}&sortKey=date&sortDirection=descending";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);

            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var records = doc.RootElement.TryGetProperty("records", out var r) ? r : doc.RootElement;

            var items = new List<DownloadItem>();
            if (records.ValueKind == JsonValueKind.Array)
            {
                foreach (var rec in records.EnumerateArray())
                {
                    var item = MapRecord(rec);
                    if (item is not null)
                        items.Add(item);
                }
            }

            var version = await GetVersionAsync(ct);
            return (items.Take(limit).ToList(), new ServiceHealth(Source.ToString().ToLowerInvariant(), Source.ToString(), true, true, null, version));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth(Source.ToString().ToLowerInvariant(), Source.ToString(), true, false, ex.Message, null));
        }
    }

    protected virtual DownloadItem? MapRecord(JsonElement rec)
    {
        var eventType = rec.TryGetProperty("eventType", out var ev) ? ev.GetString() : null;
        if (eventType is null || !IsRelevantEvent(eventType))
            return null;

        var title = rec.TryGetProperty("sourceTitle", out var st) ? st.GetString() ?? "Unknown" : "Unknown";
        var date = rec.TryGetProperty("date", out var d) && DateTimeOffset.TryParse(d.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        var quality = rec.TryGetProperty("quality", out var q) && q.TryGetProperty("quality", out var qn)
            ? qn.GetProperty("name").GetString()
            : null;

        var poster = BuildPosterUrl(rec);
        var id = rec.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : Guid.NewGuid().ToString("N");

        return new DownloadItem(
            $"{Source}-{id}",
            Source,
            DefaultMediaType,
            title,
            SubtitleFor(rec),
            MapEvent(eventType),
            date,
            poster,
            quality,
            null);
    }

    protected string? ServiceBaseUrl =>
        string.IsNullOrWhiteSpace(Options.Url) ? null : Options.Url.TrimEnd('/');

    protected virtual string? SubtitleFor(JsonElement rec) => null;

    protected virtual string? BuildPosterUrl(JsonElement rec)
    {
        if (rec.TryGetProperty("seriesId", out var seriesId))
            return PosterUrls.Sonarr(seriesId.GetInt32());

        if (rec.TryGetProperty("movieId", out var movieId))
            return PosterUrls.Radarr(movieId.GetInt32());

        if (rec.TryGetProperty("bookId", out var bookId))
            return PosterUrls.ChaptarrBook(bookId.GetInt32());

        if (rec.TryGetProperty("authorId", out var authorId))
            return PosterUrls.ChaptarrAuthor(authorId.GetInt32());

        if (rec.TryGetProperty("artistId", out var artistId))
            return PosterUrls.Lidarr(artistId.GetInt32());

        return null;
    }

    protected async Task<JsonElement?> GetJsonAsync(string path, CancellationToken ct)
    {
        var url = $"{Options.Url.TrimEnd('/')}{path}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", Options.ApiKey);
        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    protected static bool IsRelevantEvent(string eventType) =>
        IsImportEvent(eventType) || eventType is "downloadFailed";

    protected static bool IsImportEvent(string eventType) =>
        eventType is "downloadFolderImported" or "trackFileImported" or "bookFileImported";

    protected static DownloadEvent MapEvent(string eventType) => eventType switch
    {
        "grabbed" => DownloadEvent.Grabbed,
        "downloadFolderImported" or "trackFileImported" or "bookFileImported" => DownloadEvent.Imported,
        "downloadFailed" => DownloadEvent.Failed,
        _ => DownloadEvent.Other
    };

    protected static int EventPriority(DownloadEvent ev) => ev switch
    {
        DownloadEvent.Imported => 3,
        DownloadEvent.Grabbed => 2,
        DownloadEvent.Failed => 1,
        _ => 0
    };

    private async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{Options.Url.TrimEnd('/')}/api/{ApiVersion}/system/status";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class SonarrClient(HttpClient http, MediaServiceOptionsAccessor options)
    : ArrClientBase(http, options, o => o.Sonarr, "v3", MediaSource.Sonarr, MediaType.Tv)
{
    private sealed record ParsedHistoryEntry(
        int HistoryId,
        int SeriesId,
        int EpisodeId,
        string? DownloadId,
        DownloadEvent Event,
        DateTimeOffset Timestamp,
        string? Quality,
        string? PosterUrl);

    public override async Task<(IReadOnlyList<DownloadItem> Items, ServiceHealth Health)> FetchRecentAsync(
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("sonarr", "Sonarr", false, false, null, null));

        try
        {
            var fetchSize = Math.Clamp(limit * 4, limit, 200);
            var url = $"{Options.Url.TrimEnd('/')}/api/v3/history?pageSize={fetchSize}&sortKey=date&sortDirection=descending";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var records = doc.RootElement.GetProperty("records");

            var entries = new List<ParsedHistoryEntry>();
            foreach (var rec in records.EnumerateArray())
            {
                var eventType = rec.TryGetProperty("eventType", out var ev) ? ev.GetString() : null;
                if (eventType is null || !IsImportEvent(eventType))
                    continue;

                if (!rec.TryGetProperty("seriesId", out var seriesIdEl) ||
                    !rec.TryGetProperty("episodeId", out var episodeIdEl))
                    continue;

                var date = rec.TryGetProperty("date", out var d) && DateTimeOffset.TryParse(d.GetString(), out var parsed)
                    ? parsed
                    : DateTimeOffset.UtcNow;

                var quality = rec.TryGetProperty("quality", out var q) && q.TryGetProperty("quality", out var qn)
                    ? qn.GetProperty("name").GetString()
                    : null;

                var downloadId = rec.TryGetProperty("downloadId", out var dl) ? dl.GetString() : null;
                var historyId = rec.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                var seriesId = seriesIdEl.GetInt32();
                var episodeId = episodeIdEl.GetInt32();

                entries.Add(new ParsedHistoryEntry(
                    historyId,
                    seriesId,
                    episodeId,
                    downloadId,
                    DownloadEvent.Imported,
                    date,
                    quality,
                    PosterUrls.Sonarr(seriesId)));
            }

            var deduped = entries
                .GroupBy(e => e.EpisodeId)
                .Select(g => g.OrderByDescending(e => e.Timestamp).First())
                .ToList();

            var seriesIds = deduped.Select(e => e.SeriesId).Distinct().ToList();
            var episodeIds = deduped.Select(e => e.EpisodeId).Distinct().ToList();
            var seriesLookup = await LoadSeriesLookupAsync(seriesIds, ct);
            var episodeLookup = await LoadEpisodeLookupAsync(episodeIds, ct);
            var seriesEpisodes = await LoadSeriesEpisodesAsync(seriesIds, ct);

            var consumed = new HashSet<int>();
            var items = new List<DownloadItem>();

            foreach (var group in deduped
                         .Where(e => !string.IsNullOrWhiteSpace(e.DownloadId))
                         .GroupBy(e => (e.SeriesId, e.DownloadId!))
                         .Where(g => g.Count() >= 2))
            {
                var episodes = group
                    .GroupBy(e => e.EpisodeId)
                    .Select(g => g.OrderByDescending(e => e.Timestamp).First())
                    .ToList();

                foreach (var episode in episodes)
                    consumed.Add(episode.EpisodeId);

                items.Add(BuildDownloadItem(episodes, seriesLookup, episodeLookup, seriesEpisodes, group.Key.Item2));
            }

            var remaining = deduped.Where(e => !consumed.Contains(e.EpisodeId)).ToList();
            foreach (var seasonGroup in remaining
                         .GroupBy(e => (e.SeriesId, GetSeasonNumber(e, episodeLookup)))
                         .Where(g => g.Key.Item2 > 0))
            {
                var episodes = seasonGroup
                    .GroupBy(e => e.EpisodeId)
                    .Select(g => g.OrderByDescending(e => e.Timestamp).First())
                    .ToList();

                foreach (var cluster in ClusterByImportWindow(episodes, TimeSpan.FromHours(72)))
                {
                    if (cluster.Count < 2)
                        continue;

                    var ordered = cluster
                        .OrderBy(e => GetEpisodeNumber(e, episodeLookup))
                        .ToList();

                    foreach (var episode in ordered)
                        consumed.Add(episode.EpisodeId);

                    items.Add(BuildDownloadItem(
                        ordered,
                        seriesLookup,
                        episodeLookup,
                        seriesEpisodes,
                        $"s{GetSeasonNumber(ordered[0], episodeLookup)}-{ordered[0].HistoryId}"));
                }
            }

            foreach (var entry in deduped.Where(e => !consumed.Contains(e.EpisodeId)))
                items.Add(BuildDownloadItem([entry], seriesLookup, episodeLookup, seriesEpisodes, null));

            var version = await GetVersionAsync(ct);
            return (items.OrderByDescending(i => i.Timestamp).Take(limit).ToList(),
                new ServiceHealth("sonarr", "Sonarr", true, true, null, version));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("sonarr", "Sonarr", true, false, ex.Message, null));
        }
    }

    public async Task<(bool Ok, string Message)> SearchEpisodeAsync(
        int seriesId,
        int seasonNumber,
        int episodeNumber,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, "Sonarr is not configured.");

        try
        {
            var episodes = await GetJsonAsync($"/api/v3/episode?seriesId={seriesId}", ct);
            if (episodes is null || episodes.Value.ValueKind != JsonValueKind.Array)
                return (false, "Could not load episodes from Sonarr.");

            int? episodeId = null;
            foreach (var episode in episodes.Value.EnumerateArray())
            {
                if (!episode.TryGetProperty("seasonNumber", out var seasonEl) ||
                    seasonEl.GetInt32() != seasonNumber)
                    continue;

                if (!episode.TryGetProperty("episodeNumber", out var numberEl) ||
                    numberEl.GetInt32() != episodeNumber)
                    continue;

                if (episode.TryGetProperty("id", out var idEl))
                    episodeId = idEl.GetInt32();

                break;
            }

            if (episodeId is null)
                return (false, $"S{seasonNumber:00}E{episodeNumber:00} was not found in Sonarr.");

            var ok = await PostCommandAsync(new { name = "EpisodeSearch", episodeIds = new[] { episodeId.Value } }, ct);
            return ok
                ? (true, $"Sonarr is searching for S{seasonNumber:00}E{episodeNumber:00}.")
                : (false, "Sonarr rejected the episode search command.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<LibraryStatItem?> FetchLibraryStatsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var series = await GetJsonAsync("/api/v3/series", ct);
        if (series is null || series.Value.ValueKind != JsonValueKind.Array)
            return null;

        var showCount = 0;
        long episodeFiles = 0;
        long sizeOnDisk = 0;

        foreach (var item in series.Value.EnumerateArray())
        {
            showCount++;
            if (!item.TryGetProperty("statistics", out var stats))
                continue;

            episodeFiles += stats.TryGetProperty("episodeFileCount", out var files) ? files.GetInt32() : 0;
            sizeOnDisk += stats.TryGetProperty("sizeOnDisk", out var size) ? size.GetInt64() : 0;
        }

        var headline = showCount == 1 ? "1 show" : $"{CountDisplayHelper.Format(showCount)} shows";
        var detail = episodeFiles > 0
            ? $"{CountDisplayHelper.Format(episodeFiles)} eps · {ByteDisplayHelper.Format(sizeOnDisk)}"
            : ByteDisplayHelper.Format(sizeOnDisk);

        return new LibraryStatItem(
            "sonarr",
            "TV",
            headline,
            detail,
            "#35c5f4",
            Options.Url.TrimEnd('/'),
            showCount);
    }

    private static List<List<ParsedHistoryEntry>> ClusterByImportWindow(
        IReadOnlyList<ParsedHistoryEntry> episodes,
        TimeSpan maxGap)
    {
        if (episodes.Count == 0)
            return [];

        var sorted = episodes.OrderBy(e => e.Timestamp).ToList();
        var clusters = new List<List<ParsedHistoryEntry>>();
        var current = new List<ParsedHistoryEntry> { sorted[0] };

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Timestamp - sorted[i - 1].Timestamp <= maxGap)
                current.Add(sorted[i]);
            else
            {
                clusters.Add(current);
                current = [sorted[i]];
            }
        }

        clusters.Add(current);
        return clusters;
    }

    private DownloadItem BuildDownloadItem(
        IReadOnlyList<ParsedHistoryEntry> entries,
        Dictionary<int, SeriesInfo> seriesLookup,
        Dictionary<int, (int SeasonNumber, int EpisodeNumber)> episodeLookup,
        Dictionary<int, List<SeasonEpisodeInfo>> seriesEpisodes,
        string? batchKey)
    {
        var latest = entries.OrderByDescending(e => e.Timestamp).First();
        seriesLookup.TryGetValue(latest.SeriesId, out var seriesInfo);

        var episodeNumbers = entries
            .Select(e => GetEpisodeNumber(e, episodeLookup))
            .Where(n => n > 0)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var season = entries
            .Select(e => GetSeasonNumber(e, episodeLookup))
            .FirstOrDefault(n => n > 0);

        var subtitle = season > 0 && episodeNumbers.Count > 0
            ? DownloadDisplayHelper.FormatSeasonLabel(season)
            : null;

        IReadOnlyList<int>? badgeEpisodeNumbers = null;
        IReadOnlyList<int>? onDiskEpisodeNumbers = null;
        IReadOnlyList<int>? unairedEpisodeNumbers = null;
        IReadOnlyDictionary<int, DateTimeOffset>? episodeAirDates = null;
        if (episodeNumbers.Count > 0 &&
            season > 0 &&
            seriesEpisodes.TryGetValue(latest.SeriesId, out var seasonEpisodes))
        {
            var seasonAvailability = seasonEpisodes
                .Where(e => e.SeasonNumber == season)
                .Select(e => new DownloadDisplayHelper.EpisodeAvailability(
                    e.EpisodeNumber,
                    e.HasFile,
                    e.AirDateUtc))
                .ToList();

            badgeEpisodeNumbers = DownloadDisplayHelper.BuildBadgeEpisodeNumbers(episodeNumbers, seasonAvailability);
            onDiskEpisodeNumbers = DownloadDisplayHelper.BuildOnDiskEpisodeNumbers(seasonAvailability);
            unairedEpisodeNumbers = DownloadDisplayHelper.BuildUnairedEpisodeNumbers(seasonAvailability);
            episodeAirDates = DownloadDisplayHelper.BuildEpisodeAirDates(seasonAvailability);
        }

        var id = episodeNumbers.Count >= 2
            ? $"{Source}-batch-{latest.SeriesId}-{batchKey ?? latest.DownloadId ?? "group"}"
            : $"{Source}-{latest.HistoryId}";

        return new DownloadItem(
            id,
            Source,
            DefaultMediaType,
            seriesInfo.Title ?? "Unknown series",
            subtitle,
            latest.Event,
            entries.Max(e => e.Timestamp),
            latest.PosterUrl,
            latest.Quality,
            null,
            season > 0 ? season : null,
            episodeNumbers.Count > 0 ? episodeNumbers : null,
            ExternalUrl: ServiceDeepLinkBuilder.BuildItemUrl(Source, ServiceBaseUrl, seriesInfo.Slug),
            SeriesId: latest.SeriesId,
            BadgeEpisodeNumbers: badgeEpisodeNumbers,
            OnDiskEpisodeNumbers: onDiskEpisodeNumbers,
            UnairedEpisodeNumbers: unairedEpisodeNumbers,
            EpisodeAirDates: episodeAirDates,
            ImdbId: seriesInfo.ImdbId,
            TmdbId: seriesInfo.TmdbId,
            TvdbId: seriesInfo.TvdbId);
    }

    private sealed record SeasonEpisodeInfo(int SeasonNumber, int EpisodeNumber, bool HasFile, DateTimeOffset? AirDateUtc);

    private readonly record struct SeriesInfo(string? Title, string? Slug, string? ImdbId, int? TmdbId, int? TvdbId);

    private static int GetSeasonNumber(
        ParsedHistoryEntry entry,
        Dictionary<int, (int SeasonNumber, int EpisodeNumber)> episodeLookup) =>
        episodeLookup.TryGetValue(entry.EpisodeId, out var info) ? info.SeasonNumber : 0;

    private static int GetEpisodeNumber(
        ParsedHistoryEntry entry,
        Dictionary<int, (int SeasonNumber, int EpisodeNumber)> episodeLookup) =>
        episodeLookup.TryGetValue(entry.EpisodeId, out var info) ? info.EpisodeNumber : 0;

    private async Task<Dictionary<int, SeriesInfo>> LoadSeriesLookupAsync(IReadOnlyList<int> seriesIds, CancellationToken ct)
    {
        var lookup = new Dictionary<int, SeriesInfo>();
        if (seriesIds.Count == 0)
            return lookup;

        var allSeries = await GetJsonAsync("/api/v3/series", ct);
        if (allSeries is null || allSeries.Value.ValueKind != JsonValueKind.Array)
            return lookup;

        var wanted = seriesIds.ToHashSet();
        foreach (var series in allSeries.Value.EnumerateArray())
        {
            if (!series.TryGetProperty("id", out var idEl))
                continue;

            var id = idEl.GetInt32();
            if (!wanted.Contains(id))
                continue;

            var title = series.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var slug = series.TryGetProperty("titleSlug", out var slugEl) ? slugEl.GetString() : null;
            var imdbId = series.TryGetProperty("imdbId", out var imdbEl) ? imdbEl.GetString() : null;
            int? tvdbId = series.TryGetProperty("tvdbId", out var tvdbEl) ? tvdbEl.GetInt32() : null;
            int? tmdbId = series.TryGetProperty("tmdbId", out var tmdbEl) ? tmdbEl.GetInt32() : null;
            lookup[id] = new SeriesInfo(title, slug, imdbId, tmdbId, tvdbId);
        }

        return lookup;
    }

    private async Task<Dictionary<int, (int SeasonNumber, int EpisodeNumber)>> LoadEpisodeLookupAsync(
        IReadOnlyList<int> episodeIds,
        CancellationToken ct)
    {
        var lookup = new Dictionary<int, (int, int)>();
        if (episodeIds.Count == 0)
            return lookup;

        var query = string.Join("&", episodeIds.Select(id => $"episodeIds={id}"));
        var episodes = await GetJsonAsync($"/api/v3/episode?{query}", ct);
        if (episodes is null || episodes.Value.ValueKind != JsonValueKind.Array)
            return lookup;

        foreach (var episode in episodes.Value.EnumerateArray())
        {
            if (!episode.TryGetProperty("id", out var idEl))
                continue;

            var season = episode.TryGetProperty("seasonNumber", out var seasonEl) ? seasonEl.GetInt32() : 0;
            var number = episode.TryGetProperty("episodeNumber", out var numberEl) ? numberEl.GetInt32() : 0;
            lookup[idEl.GetInt32()] = (season, number);
        }

        return lookup;
    }

    private async Task<Dictionary<int, List<SeasonEpisodeInfo>>> LoadSeriesEpisodesAsync(
        IReadOnlyCollection<int> seriesIds,
        CancellationToken ct)
    {
        var lookup = new Dictionary<int, List<SeasonEpisodeInfo>>();
        foreach (var seriesId in seriesIds.Distinct())
        {
            var episodes = await GetJsonAsync($"/api/v3/episode?seriesId={seriesId}", ct);
            if (episodes is null || episodes.Value.ValueKind != JsonValueKind.Array)
                continue;

            var seasonEpisodes = new List<SeasonEpisodeInfo>();
            foreach (var episode in episodes.Value.EnumerateArray())
            {
                if (!episode.TryGetProperty("seasonNumber", out var seasonEl) ||
                    !episode.TryGetProperty("episodeNumber", out var numberEl))
                    continue;

                var hasFile = episode.TryGetProperty("hasFile", out var hasFileEl) && hasFileEl.GetBoolean();
                DateTimeOffset? airDateUtc = null;
                if (episode.TryGetProperty("airDateUtc", out var airEl) &&
                    DateTimeOffset.TryParse(airEl.GetString(), out var parsedAir))
                    airDateUtc = parsedAir;

                seasonEpisodes.Add(new SeasonEpisodeInfo(
                    seasonEl.GetInt32(),
                    numberEl.GetInt32(),
                    hasFile,
                    airDateUtc));
            }

            if (seasonEpisodes.Count > 0)
                lookup[seriesId] = seasonEpisodes;
        }

        return lookup;
    }

    private async Task<bool> PostCommandAsync(object body, CancellationToken ct)
    {
        var url = $"{Options.Url.TrimEnd('/')}/api/v3/command";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Api-Key", Options.ApiKey);
        request.Content = JsonContent.Create(body);
        using var response = await Http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{Options.Url.TrimEnd('/')}/api/v3/system/status";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class RadarrClient(HttpClient http, MediaServiceOptionsAccessor options)
    : ArrClientBase(http, options, o => o.Radarr, "v3", MediaSource.Radarr, MediaType.Movie)
{
    public override async Task<(IReadOnlyList<DownloadItem> Items, ServiceHealth Health)> FetchRecentAsync(
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("radarr", "Radarr", false, false, null, null));

        try
        {
            var fetchSize = Math.Clamp(limit * 2, limit, 100);
            var url = $"{Options.Url.TrimEnd('/')}/api/v3/history?pageSize={fetchSize}&sortKey=date&sortDirection=descending";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var records = doc.RootElement.GetProperty("records");

            var parsed = new List<(int HistoryId, int MovieId, DownloadEvent Event, DateTimeOffset Timestamp, string? Quality, string SourceTitle)>();
            foreach (var rec in records.EnumerateArray())
            {
                var eventType = rec.TryGetProperty("eventType", out var ev) ? ev.GetString() : null;
                if (eventType is null || !IsImportEvent(eventType))
                    continue;

                if (!rec.TryGetProperty("movieId", out var movieIdEl))
                    continue;

                var date = rec.TryGetProperty("date", out var d) && DateTimeOffset.TryParse(d.GetString(), out var parsedDate)
                    ? parsedDate
                    : DateTimeOffset.UtcNow;

                var quality = rec.TryGetProperty("quality", out var q) && q.TryGetProperty("quality", out var qn)
                    ? qn.GetProperty("name").GetString()
                    : null;

                var sourceTitle = rec.TryGetProperty("sourceTitle", out var st) ? st.GetString() ?? "Unknown" : "Unknown";
                var historyId = rec.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;

                parsed.Add((historyId, movieIdEl.GetInt32(), DownloadEvent.Imported, date, quality, sourceTitle));
            }

            var deduped = parsed
                .GroupBy(p => p.MovieId)
                .Select(g => g.OrderByDescending(p => p.Timestamp).First())
                .ToList();

            var movieLookup = await LoadMovieLookupAsync(deduped.Select(p => p.MovieId).Distinct().ToList(), ct);

            var items = deduped
                .Select(entry =>
                {
                    var hasMovie = movieLookup.TryGetValue(entry.MovieId, out var movie);
                    var title = hasMovie ? movie.Title : ParseMovieTitle(entry.SourceTitle);
                    var subtitle = hasMovie && movie.Year > 0 ? movie.Year.ToString() : null;
                    var poster = PosterUrls.Radarr(entry.MovieId);

                    return new DownloadItem(
                        $"{Source}-{entry.HistoryId}",
                        Source,
                        DefaultMediaType,
                        title,
                        subtitle,
                        entry.Event,
                        entry.Timestamp,
                        poster,
                        entry.Quality,
                        null,
                        ExternalUrl: ServiceDeepLinkBuilder.BuildItemUrl(Source, ServiceBaseUrl, hasMovie ? movie.Slug : null),
                        ImdbId: hasMovie ? movie.ImdbId : null,
                        TmdbId: hasMovie ? movie.TmdbId : null,
                        YouTubeTrailerId: hasMovie ? movie.YouTubeTrailerId : null);
                })
                .OrderByDescending(i => i.Timestamp)
                .Take(limit)
                .ToList();

            var version = await GetVersionAsync(ct);
            return (items, new ServiceHealth("radarr", "Radarr", true, true, null, version));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("radarr", "Radarr", true, false, ex.Message, null));
        }
    }

    public async Task<LibraryStatItem?> FetchLibraryStatsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var movies = await GetJsonAsync("/api/v3/movie", ct);
        if (movies is null || movies.Value.ValueKind != JsonValueKind.Array)
            return null;

        var total = 0;
        var onDisk = 0;
        long sizeOnDisk = 0;

        foreach (var item in movies.Value.EnumerateArray())
        {
            total++;
            if (item.TryGetProperty("hasFile", out var hasFile) && hasFile.GetBoolean())
                onDisk++;

            sizeOnDisk += item.TryGetProperty("sizeOnDisk", out var size) ? size.GetInt64() : 0;
        }

        var headline = total == 1 ? "1 film" : $"{CountDisplayHelper.Format(total)} films";
        var detail = onDisk > 0
            ? $"{CountDisplayHelper.Format(onDisk)} on disk · {ByteDisplayHelper.Format(sizeOnDisk)}"
            : ByteDisplayHelper.Format(sizeOnDisk);

        return new LibraryStatItem(
            "radarr",
            "Movies",
            headline,
            detail,
            "#f5c518",
            Options.Url.TrimEnd('/'),
            total);
    }

    private async Task<Dictionary<int, MovieInfo>> LoadMovieLookupAsync(
        IReadOnlyList<int> movieIds,
        CancellationToken ct)
    {
        var lookup = new Dictionary<int, MovieInfo>();
        if (movieIds.Count == 0)
            return lookup;

        var allMovies = await GetJsonAsync("/api/v3/movie", ct);
        if (allMovies is null || allMovies.Value.ValueKind != JsonValueKind.Array)
            return lookup;

        var wanted = movieIds.ToHashSet();
        foreach (var movie in allMovies.Value.EnumerateArray())
        {
            if (!movie.TryGetProperty("id", out var idEl))
                continue;

            var id = idEl.GetInt32();
            if (!wanted.Contains(id))
                continue;

            var title = movie.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "Unknown" : "Unknown";
            var year = movie.TryGetProperty("year", out var yearEl) ? yearEl.GetInt32() : 0;
            var slug = movie.TryGetProperty("titleSlug", out var slugEl) ? slugEl.GetString() : null;
            var imdbId = movie.TryGetProperty("imdbId", out var imdbEl) ? imdbEl.GetString() : null;
            int? tmdbId = movie.TryGetProperty("tmdbId", out var tmdbEl) ? tmdbEl.GetInt32() : null;
            var youtubeTrailerId = movie.TryGetProperty("youTubeTrailerId", out var trailerEl) ? trailerEl.GetString() : null;
            lookup[id] = new MovieInfo(title, year, slug, imdbId, tmdbId, youtubeTrailerId);
        }

        return lookup;
    }

    private readonly record struct MovieInfo(string Title, int Year, string? Slug, string? ImdbId, int? TmdbId, string? YouTubeTrailerId);

    private static string ParseMovieTitle(string sourceTitle)
    {
        var match = System.Text.RegularExpressions.Regex.Match(sourceTitle, @"^(.+?)\s*\((\d{4})\)");
        return match.Success ? match.Groups[1].Value.Trim() : sourceTitle;
    }

    private async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{Options.Url.TrimEnd('/')}/api/v3/system/status";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class LidarrClient(HttpClient http, MediaServiceOptionsAccessor options)
    : ArrClientBase(http, options, o => o.Lidarr, "v1", MediaSource.Lidarr, MediaType.Music)
{
    public override async Task<(IReadOnlyList<DownloadItem> Items, ServiceHealth Health)> FetchRecentAsync(
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("lidarr", "Lidarr", false, false, null, null));

        try
        {
            var fetchSize = Math.Clamp(limit * 2, limit, 100);
            var url = $"{Options.Url.TrimEnd('/')}/api/v1/history?pageSize={fetchSize}&sortKey=date&sortDirection=descending";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var records = doc.RootElement.TryGetProperty("records", out var r) ? r : doc.RootElement;

            var parsed = new List<(int HistoryId, int ArtistId, DownloadEvent Event, DateTimeOffset Timestamp, string? Quality, string SourceTitle)>();
            if (records.ValueKind == JsonValueKind.Array)
            {
                foreach (var rec in records.EnumerateArray())
                {
                    var eventType = rec.TryGetProperty("eventType", out var ev) ? ev.GetString() : null;
                    if (eventType is null || !IsImportEvent(eventType))
                        continue;

                    if (!rec.TryGetProperty("artistId", out var artistIdEl))
                        continue;

                    var date = rec.TryGetProperty("date", out var d) && DateTimeOffset.TryParse(d.GetString(), out var parsedDate)
                        ? parsedDate
                        : DateTimeOffset.UtcNow;

                    var quality = rec.TryGetProperty("quality", out var q) && q.TryGetProperty("quality", out var qn)
                        ? qn.GetProperty("name").GetString()
                        : null;

                    var sourceTitle = rec.TryGetProperty("sourceTitle", out var st) ? st.GetString() ?? "Unknown" : "Unknown";
                    var historyId = rec.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;

                    parsed.Add((historyId, artistIdEl.GetInt32(), DownloadEvent.Imported, date, quality, sourceTitle));
                }
            }

            var deduped = parsed
                .GroupBy(p => p.ArtistId)
                .Select(g => g.OrderByDescending(p => p.Timestamp).First())
                .ToList();

            var artistLookup = await LoadArtistLookupAsync(deduped.Select(p => p.ArtistId).Distinct().ToList(), ct);
            var items = deduped
                .Select(entry =>
                {
                    var hasArtist = artistLookup.TryGetValue(entry.ArtistId, out var artist);
                    var title = hasArtist ? artist.Name : entry.SourceTitle;
                    var poster = PosterUrls.Lidarr(entry.ArtistId);

                    return new DownloadItem(
                        $"{Source}-{entry.HistoryId}",
                        Source,
                        DefaultMediaType,
                        title,
                        null,
                        entry.Event,
                        entry.Timestamp,
                        poster,
                        entry.Quality,
                        null,
                        ExternalUrl: ServiceDeepLinkBuilder.BuildItemUrl(Source, ServiceBaseUrl, hasArtist ? artist.Slug : null));
                })
                .OrderByDescending(i => i.Timestamp)
                .Take(limit)
                .ToList();

            var version = await GetVersionAsync(ct);
            return (items, new ServiceHealth("lidarr", "Lidarr", true, true, null, version));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("lidarr", "Lidarr", true, false, ex.Message, null));
        }
    }

    private async Task<Dictionary<int, ArtistInfo>> LoadArtistLookupAsync(IReadOnlyList<int> artistIds, CancellationToken ct)
    {
        var lookup = new Dictionary<int, ArtistInfo>();
        if (artistIds.Count == 0)
            return lookup;

        var allArtists = await GetJsonAsync("/api/v1/artist", ct);
        if (allArtists is null || allArtists.Value.ValueKind != JsonValueKind.Array)
            return lookup;

        var wanted = artistIds.ToHashSet();
        foreach (var artist in allArtists.Value.EnumerateArray())
        {
            if (!artist.TryGetProperty("id", out var idEl))
                continue;

            var id = idEl.GetInt32();
            if (!wanted.Contains(id))
                continue;

            var name = artist.TryGetProperty("artistName", out var nameEl) ? nameEl.GetString() ?? "Unknown" : "Unknown";
            var slug = artist.TryGetProperty("titleSlug", out var slugEl) ? slugEl.GetString() : null;
            lookup[id] = new ArtistInfo(name, slug);
        }

        return lookup;
    }

    private readonly record struct ArtistInfo(string Name, string? Slug);

    private async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{Options.Url.TrimEnd('/')}/api/v1/system/status";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<LibraryStatItem?> FetchLibraryStatsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        var artists = await GetJsonAsync("/api/v1/artist", ct);
        if (artists is null || artists.Value.ValueKind != JsonValueKind.Array)
            return null;

        var artistCount = 0;
        long trackFiles = 0;
        long sizeOnDisk = 0;

        foreach (var item in artists.Value.EnumerateArray())
        {
            artistCount++;
            if (!item.TryGetProperty("statistics", out var stats))
                continue;

            trackFiles += stats.TryGetProperty("trackFileCount", out var files) ? files.GetInt32() : 0;
            sizeOnDisk += stats.TryGetProperty("sizeOnDisk", out var size) ? size.GetInt64() : 0;
        }

        var headline = artistCount == 1 ? "1 artist" : $"{CountDisplayHelper.Format(artistCount)} artists";
        var detail = trackFiles > 0
            ? $"{CountDisplayHelper.Format(trackFiles)} tracks · {ByteDisplayHelper.Format(sizeOnDisk)}"
            : ByteDisplayHelper.Format(sizeOnDisk);

        return new LibraryStatItem(
            "lidarr",
            "Music",
            headline,
            detail,
            "#bc93e1",
            Options.Url.TrimEnd('/'),
            artistCount);
    }
}

public sealed class ChaptarrClient(HttpClient http, MediaServiceOptionsAccessor options)
    : ArrClientBase(http, options, o => o.Chaptarr, "v1", MediaSource.Chaptarr, MediaType.Audiobook)
{
    public override async Task<(IReadOnlyList<DownloadItem> Items, ServiceHealth Health)> FetchRecentAsync(
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("chaptarr", "Chaptarr", false, false, null, null));

        try
        {
            var fetchSize = Math.Clamp(limit * 2, limit, 100);
            var url = $"{Options.Url.TrimEnd('/')}/api/v1/history?pageSize={fetchSize}&sortKey=date&sortDirection=descending";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);

            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var records = doc.RootElement.TryGetProperty("records", out var r) ? r : doc.RootElement;

            var pending = new List<(DownloadItem Item, int BookId)>();
            if (records.ValueKind == JsonValueKind.Array)
            {
                foreach (var rec in records.EnumerateArray())
                {
                    if (!rec.TryGetProperty("bookId", out var bookIdEl))
                        continue;

                    var item = MapRecord(rec);
                    if (item is not null && item.MediaType == MediaType.Audiobook)
                        pending.Add((item, bookIdEl.GetInt32()));
                }
            }

            var bookLookup = await LoadBookLookupAsync(pending.Select(p => p.BookId).ToHashSet(), ct);
            var items = pending
                .Select(entry =>
                {
                    if (!bookLookup.TryGetValue(entry.BookId, out var book))
                        return (BookId: entry.BookId, Item: entry.Item);

                    var chaptarrUrl = ServiceDeepLinkBuilder.BuildItemUrl(Source, ServiceBaseUrl, book.Slug);
                    var item = entry.Item with
                    {
                        Title = book.Title ?? entry.Item.Title,
                        Subtitle = book.Author ?? entry.Item.Subtitle,
                        ExternalUrl = chaptarrUrl,
                        ChaptarrUrl = chaptarrUrl,
                        GoodreadsUrl = book.GoodreadsUrl,
                        HardcoverUrl = book.HardcoverUrl,
                        Asin = book.Asin
                    };
                    return (BookId: entry.BookId, Item: item);
                })
                .GroupBy(entry => entry.BookId)
                .Select(group =>
                {
                    var latest = group.OrderByDescending(x => x.Item.Timestamp).First();
                    return latest.Item with
                    {
                        Id = $"{Source}-book-{group.Key}",
                        Timestamp = group.Max(x => x.Item.Timestamp)
                    };
                })
                .OrderByDescending(x => x.Timestamp)
                .Take(limit)
                .ToList();

            var version = await GetVersionAsync(ct);
            return (items, new ServiceHealth("chaptarr", "Chaptarr", true, true, null, version));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("chaptarr", "Chaptarr", true, false, ex.Message, null));
        }
    }

    public async Task<LibraryStatItem?> FetchLibraryStatsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var authors = await GetJsonAsync("/api/v1/author", ct);
            if (authors is null || authors.Value.ValueKind != JsonValueKind.Array)
                return null;

            var catalogBooks = 0;
            var booksOnDisk = 0;
            var audioFiles = 0;
            long sizeOnDisk = 0;

            foreach (var author in authors.Value.EnumerateArray())
            {
                if (!author.TryGetProperty("statistics", out var stats))
                    continue;

                catalogBooks += stats.TryGetProperty("bookCount", out var books) ? books.GetInt32() : 0;
                booksOnDisk += stats.TryGetProperty("availableBookCount", out var available) ? available.GetInt32() : 0;
                audioFiles += stats.TryGetProperty("bookFileCount", out var files) ? files.GetInt32() : 0;
                sizeOnDisk += stats.TryGetProperty("sizeOnDisk", out var size) ? size.GetInt64() : 0;
            }

            if (booksOnDisk <= 0 && catalogBooks <= 0)
                return null;

            var onDiskCount = booksOnDisk > 0 ? booksOnDisk : catalogBooks;
            var headline = onDiskCount == 1 ? "1 book on disk" : $"{CountDisplayHelper.Format(onDiskCount)} books on disk";

            var detailParts = new List<string>();
            if (catalogBooks > onDiskCount)
                detailParts.Add($"{CountDisplayHelper.Format(catalogBooks)} tracked in catalog");
            if (audioFiles > onDiskCount)
                detailParts.Add($"{CountDisplayHelper.Format(audioFiles)} audio files");
            if (sizeOnDisk > 0)
                detailParts.Add(ByteDisplayHelper.Format(sizeOnDisk));

            return new LibraryStatItem(
                "chaptarr",
                "Chaptarr",
                headline,
                detailParts.Count > 0 ? string.Join(" · ", detailParts) : null,
                "#00d2be",
                Options.Url.TrimEnd('/'),
                onDiskCount);
        }
        catch
        {
            return null;
        }
    }

    protected override DownloadItem? MapRecord(JsonElement rec)
    {
        var eventType = rec.TryGetProperty("eventType", out var ev) ? ev.GetString() : null;
        if (eventType is null || !IsImportEvent(eventType))
            return null;

        var title = rec.TryGetProperty("sourceTitle", out var st) ? st.GetString() ?? "Unknown" : "Unknown";
        string? subtitle = null;

        if (rec.TryGetProperty("book", out var book))
        {
            if (book.TryGetProperty("title", out var bookTitle) && !string.IsNullOrWhiteSpace(bookTitle.GetString()))
                title = bookTitle.GetString()!;

            if (book.TryGetProperty("mediaType", out var mt) &&
                !string.Equals(mt.GetString(), "audiobook", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        if (rec.TryGetProperty("author", out var author) &&
            author.TryGetProperty("authorName", out var authorName))
            subtitle = authorName.GetString();

        var date = rec.TryGetProperty("date", out var d) && DateTimeOffset.TryParse(d.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        var quality = rec.TryGetProperty("quality", out var q) && q.TryGetProperty("quality", out var qn)
            ? qn.GetProperty("name").GetString()
            : null;

        var poster = BuildPosterUrl(rec);
        var id = rec.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : Guid.NewGuid().ToString("N");

        return new DownloadItem(
            $"{Source}-{id}",
            Source,
            MediaType.Audiobook,
            title,
            subtitle,
            DownloadEvent.Imported,
            date,
            poster,
            quality,
            null);
    }

    private async Task<Dictionary<int, BookInfo>> LoadBookLookupAsync(IReadOnlyCollection<int> bookIds, CancellationToken ct)
    {
        var lookup = new Dictionary<int, BookInfo>();
        if (bookIds.Count == 0)
            return lookup;

        foreach (var batch in bookIds.Distinct().Chunk(40))
        {
            var query = string.Join("&", batch.Select(id => $"bookIds={id}"));
            var books = await GetJsonAsync($"/api/v1/book?{query}", ct);
            if (books is null || books.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var book in books.Value.EnumerateArray())
            {
                if (!book.TryGetProperty("id", out var idEl))
                    continue;

                var id = idEl.GetInt32();
                var title = book.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                var slug = book.TryGetProperty("titleSlug", out var slugEl) ? slugEl.GetString() : null;
                string? author = null;
                if (book.TryGetProperty("author", out var authorEl) &&
                    authorEl.TryGetProperty("authorName", out var authorNameEl))
                    author = authorNameEl.GetString();

                var asin = book.TryGetProperty("audibleASIN", out var audibleAsinEl) &&
                           !string.IsNullOrWhiteSpace(audibleAsinEl.GetString())
                    ? audibleAsinEl.GetString()
                    : book.TryGetProperty("asin", out var asinEl)
                        ? asinEl.GetString()
                        : null;

                var (goodreadsUrl, hardcoverUrl) = ParseBookLinks(book);

                lookup[id] = new BookInfo(title, slug, author, asin, goodreadsUrl, hardcoverUrl);
            }
        }

        return lookup;
    }

    private readonly record struct BookInfo(
        string? Title,
        string? Slug,
        string? Author,
        string? Asin,
        string? GoodreadsUrl,
        string? HardcoverUrl);

    private static (string? GoodreadsUrl, string? HardcoverUrl) ParseBookLinks(JsonElement book)
    {
        string? goodreadsUrl = null;
        string? hardcoverUrl = null;

        if (!book.TryGetProperty("links", out var linksEl) || linksEl.ValueKind != JsonValueKind.Array)
            return (goodreadsUrl, hardcoverUrl);

        foreach (var link in linksEl.EnumerateArray())
        {
            var name = link.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var url = link.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
                continue;

            if (string.Equals(name, "goodreads", StringComparison.OrdinalIgnoreCase))
                goodreadsUrl = url;
            else if (string.Equals(name, "hardcover", StringComparison.OrdinalIgnoreCase))
                hardcoverUrl = url;
        }

        return (goodreadsUrl, hardcoverUrl);
    }

    private async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{Options.Url.TrimEnd('/')}/api/v1/system/status";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", Options.ApiKey);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
