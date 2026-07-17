using System.Text.Json;
using ArrDash.Configuration;
using ArrDash.Models;

namespace ArrDash.Services.Clients;

public sealed class TautulliClient(HttpClient http, MediaServiceOptionsAccessor options)
{
    private ServiceEndpoint Tautulli => options.Options.Tautulli;

    public bool IsConfigured => Tautulli.IsConfigured;

    public async Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, "Tautulli URL or API key required");

        try
        {
            var doc = await GetApiAsync("get_server_info", ct);
            if (doc is null)
                return (false, "Invalid response");

            var (plexOk, plexMessage) = await CheckPlexAuthorizationAsync(ct);
            return plexOk ? (true, "Connected") : (false, plexMessage);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<DateTimeOffset?> GetLastLoggedPlayAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var doc = await GetApiAsync("get_history", ct,
                ("length", "1"),
                ("order_column", "date"),
                ("order_dir", "desc"));

            if (doc is null)
                return null;

            var rowsElement = doc.RootElement;
            if (rowsElement.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Array)
                rowsElement = nested;

            if (rowsElement.ValueKind != JsonValueKind.Array || rowsElement.GetArrayLength() == 0)
                return null;

            return ReadUnixDate(rowsElement[0], "date", "started", "stopped");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects when Tautulli's API works but Plex session monitoring has stopped (expired token, 401 on /status/sessions).
    /// </summary>
    public async Task<(bool Ok, string Message)> CheckPlexAuthorizationAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return (false, "Tautulli URL or API key required");

        try
        {
            var doc = await GetApiAsync("get_history", ct,
                ("length", "1"),
                ("order_column", "date"),
                ("order_dir", "desc"));

            if (doc is null)
                return (false, "Could not read Tautulli history");

            var rowsElement = doc.RootElement;
            if (rowsElement.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Array)
                rowsElement = nested;

            if (rowsElement.ValueKind != JsonValueKind.Array || rowsElement.GetArrayLength() == 0)
                return (false, "Tautulli has no Plex play history — check Plex authorization in Tautulli Settings → Plex.");

            var latest = rowsElement[0];
            var lastPlayed = ReadUnixDate(latest, "date", "started", "stopped");
            if (lastPlayed is null)
                return (true, "Connected");

            var daysSince = (DateTimeOffset.UtcNow - lastPlayed.Value).TotalDays;
            if (daysSince <= 21)
                return (true, "Connected");

            return (false,
                $"Last Plex play logged {lastPlayed.Value:MMM d, yyyy} ({(int)daysSince} days ago). "
                + "Tautulli has likely lost Plex authorization — open Tautulli → Settings → Plex and sign in again.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<ActivityMediaMix?> GetPlaysByDateAsync(WatchStatsRange range, CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var timeRange = WatchStatsPeriodHelper.ToDays(range);
            var doc = await GetApiAsync("get_plays_by_date", ct,
                ("time_range", timeRange.ToString()),
                ("y_axis", "duration"));

            if (doc is null)
                return null;

            var root = doc.RootElement;
            if (!root.TryGetProperty("categories", out var categoriesEl) || categoriesEl.ValueKind != JsonValueKind.Array)
                return null;
            if (!root.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
                return null;

            var categories = categoriesEl.EnumerateArray()
                .Select(c => c.GetString() ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            var series = seriesEl.EnumerateArray()
                .Select(s => new ActivityMediaSeries(
                    ReadString(s, "name") ?? "Unknown",
                    s.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                        ? data.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0).ToList()
                        : []))
                .Where(s => s.Values.Count > 0)
                .ToList();

            return categories.Count == 0 || series.Count == 0
                ? null
                : new ActivityMediaMix(categories, series);
        }
        catch
        {
            return null;
        }
    }

    public async Task<TautulliActivityCharts?> GetActivityChartsAsync(WatchStatsRange range, CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var timeRange = WatchStatsPeriodHelper.ToDays(range);
            var dayTask = GetCategorySeriesChartAsync("get_plays_by_dayofweek", timeRange, ct);
            var hourTask = GetCategorySeriesChartAsync("get_plays_by_hourofday", timeRange, ct);
            var qualityTask = GetStreamQualityAsync(timeRange, ct);
            await Task.WhenAll(dayTask, hourTask, qualityTask);

            var byDay = await dayTask;
            var byHour = await hourTask;
            var (quality, qualityStats) = await qualityTask;

            if (byDay.Count == 0 && byHour.Count == 0 && quality.Count == 0)
                return null;

            return new TautulliActivityCharts(byDay, byHour, quality, qualityStats);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<(DateTime Date, double Count)>> GetDailyPlaysAsync(WatchStatsRange range, CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        try
        {
            var timeRange = WatchStatsPeriodHelper.ToDays(range);
            var doc = await GetApiAsync("get_plays_by_date", ct,
                ("time_range", timeRange.ToString()),
                ("y_axis", "plays"));

            if (doc is null)
                return [];

            var root = doc.RootElement;
            if (!root.TryGetProperty("categories", out var categoriesEl) || categoriesEl.ValueKind != JsonValueKind.Array)
                return [];
            if (!root.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
                return [];

            var categories = categoriesEl.EnumerateArray()
                .Select(c => c.GetString() ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToList();

            JsonElement? totalSeries = null;
            foreach (var series in seriesEl.EnumerateArray())
            {
                var name = ReadString(series, "name");
                if (string.Equals(name, "Total", StringComparison.OrdinalIgnoreCase))
                {
                    totalSeries = series;
                    break;
                }
            }

            totalSeries ??= seriesEl.EnumerateArray().FirstOrDefault();

            if (totalSeries is null || !totalSeries.Value.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                return [];

            var values = dataEl.EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0)
                .ToList();

            var (rangeStart, rangeEnd) = range.Bounds();
            var results = new List<(DateTime Date, double Count)>();
            for (var i = 0; i < Math.Min(categories.Count, values.Count); i++)
            {
                if (values[i] <= 0)
                    continue;

                if (!DateTime.TryParse(categories[i], out var date))
                    continue;

                date = date.Date;
                if (date < rangeStart || date > rangeEnd)
                    continue;

                results.Add((date, values[i]));
            }

            return results;
        }
        catch
        {
            return [];
        }
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
            var timeRange = WatchStatsPeriodHelper.ToDays(range);
            var statIds = new[]
            {
                "top_users",
                "top_movies",
                "top_tv",
                "top_music",
                "top_platforms",
                "last_watched"
            };

            var stats = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var statId in statIds)
            {
                var doc = await GetApiAsync("get_home_stats", ct,
                    ("time_range", timeRange.ToString()),
                    ("stats_type", "duration"),
                    ("count", limit.ToString()),
                    ("stat_id", statId));

                if (doc is null)
                    continue;

                if (doc.RootElement.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                    stats[statId] = rows;
            }

            var topUsers = MapUserRows(stats.GetValueOrDefault("top_users"), limit);
            var periodTotals = await GetPeriodTotalsAsync(range, ct);
            var summary = new WatchStatsSummary(
                periodTotals.Hours > 0 ? periodTotals.Hours : Math.Round(topUsers.Sum(r => r.Hours), 1),
                periodTotals.Plays > 0 ? periodTotals.Plays : topUsers.Sum(r => r.Plays),
                topUsers.Count,
                ReadLastPlay(stats.GetValueOrDefault("last_watched")));

            return new WatchStatsSourceSnapshot(
                WatchStatsSources.Plex,
                WatchStatsSources.Label(WatchStatsSources.Plex),
                true,
                null,
                summary,
                topUsers,
                [],
                MapMediaRows(stats.GetValueOrDefault("top_movies"), "movie", limit),
                MapMediaRows(stats.GetValueOrDefault("top_tv"), "episode", limit),
                MapMediaRows(stats.GetValueOrDefault("top_music"), "music", limit),
                MapPlatformRows(stats.GetValueOrDefault("top_platforms"), limit),
                MapRecentRows(stats.GetValueOrDefault("last_watched"), limit));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Household totals for the period from get_plays_by_date (not capped to top-N leaderboard rows).
    /// </summary>
    private async Task<(double Hours, int Plays)> GetPeriodTotalsAsync(WatchStatsRange range, CancellationToken ct)
    {
        var timeRange = WatchStatsPeriodHelper.ToDays(range);
        var durationTask = SumPlaysByDateAxisAsync(timeRange, "duration", ct);
        var playsTask = SumPlaysByDateAxisAsync(timeRange, "plays", ct);
        await Task.WhenAll(durationTask, playsTask);
        var seconds = await durationTask;
        var plays = await playsTask;
        return (Math.Round(seconds / 3600.0, 1), (int)Math.Round(plays));
    }

    private async Task<double> SumPlaysByDateAxisAsync(int timeRange, string yAxis, CancellationToken ct)
    {
        try
        {
            var doc = await GetApiAsync("get_plays_by_date", ct,
                ("time_range", timeRange.ToString()),
                ("y_axis", yAxis));
            if (doc is null)
                return 0;

            var root = doc.RootElement;
            if (!root.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
                return 0;

            JsonElement? total = null;
            foreach (var series in seriesEl.EnumerateArray())
            {
                if (string.Equals(ReadString(series, "name"), "Total", StringComparison.OrdinalIgnoreCase))
                {
                    total = series;
                    break;
                }
            }

            total ??= seriesEl.EnumerateArray().FirstOrDefault();
            if (total is null
                || !total.Value.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                return 0;

            return data.EnumerateArray()
                .Sum(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0);
        }
        catch
        {
            return 0;
        }
    }

    public async Task<IReadOnlyList<ImportedPlayEvent>> FetchHistoryAsync(
        DateTimeOffset sinceUtc,
        int maxRows,
        CancellationToken ct,
        IReadOnlyList<(string Key, string Value)>? extraParams = null)
    {
        if (!IsConfigured)
            return [];

        var libraryNames = await GetLibraryNameMapAsync(ct);
        const int pageSize = 100;
        const int batchSize = 4; // pages fetched concurrently per round-trip

        var results = new List<ImportedPlayEvent>();
        var start = 0;

        while (results.Count < maxRows)
        {
            var pageRequests = new List<(int Start, int Length)>();
            var budget = maxRows - results.Count;
            for (var i = 0; i < batchSize && budget > 0; i++)
            {
                var length = Math.Min(pageSize, budget);
                pageRequests.Add((start + i * pageSize, length));
                budget -= length;
            }

            var pages = await Task.WhenAll(
                pageRequests.Select(p => FetchHistoryPageAsync(p.Start, p.Length, extraParams, libraryNames, ct)));

            var stop = false;
            for (var i = 0; i < pages.Length; i++)
            {
                var page = pages[i];
                foreach (var row in page.Rows)
                {
                    if (row.PlayedAtUtc < sinceUtc)
                    {
                        stop = true;
                        break;
                    }

                    results.Add(row);
                }

                if (stop || page.ReachedUnparseableRow && page.RawRowCount == 0 || page.RawRowCount < pageRequests[i].Length)
                {
                    stop = true;
                    break;
                }
            }

            if (stop)
                break;

            start += pageRequests.Count * pageSize;
        }

        return results;
    }

    private async Task<(List<ImportedPlayEvent> Rows, bool ReachedUnparseableRow, int RawRowCount)> FetchHistoryPageAsync(
        int start,
        int length,
        IReadOnlyList<(string Key, string Value)>? extraParams,
        IReadOnlyDictionary<string, string> libraryNames,
        CancellationToken ct)
    {
        var query = new List<(string Key, string Value)>
        {
            ("start", start.ToString()),
            ("length", length.ToString())
        };
        if (extraParams is not null)
            query.AddRange(extraParams);

        var doc = await GetApiAsync("get_history", ct, query.ToArray());
        if (doc is null)
            return ([], false, 0);

        var rowsElement = doc.RootElement;
        if (rowsElement.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Array)
            rowsElement = nested;
        else if (rowsElement.ValueKind != JsonValueKind.Array)
            return ([], false, 0);

        var rawCount = rowsElement.GetArrayLength();
        var rows = new List<ImportedPlayEvent>();
        var reachedUnparseable = false;
        foreach (var row in rowsElement.EnumerateArray())
        {
            if (ReadUnixDate(row, "date", "started", "stopped") is null)
            {
                reachedUnparseable = true;
                continue;
            }

            var mapped = MapHistoryRow(row, libraryNames);
            if (mapped is not null)
                rows.Add(mapped);
        }

        return (rows, reachedUnparseable, rawCount);
    }

    public async Task<IReadOnlyList<WatchStatsLibraryInfo>> FetchLibrariesAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        try
        {
            var doc = await GetApiAsync("get_libraries", ct);
            if (doc is null)
                return [];

            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                if (root.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Array)
                    root = nested;
                else
                    return [];
            }

            var list = new List<WatchStatsLibraryInfo>();
            foreach (var row in root.EnumerateArray())
            {
                var id = ReadString(row, "section_id", "id");
                var name = ReadString(row, "section_name", "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                    continue;
                var type = ReadString(row, "section_type", "type");
                list.Add(new WatchStatsLibraryInfo(WatchStatsSources.Plex, id, name, type));
            }

            return list;
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

    public async Task<IReadOnlyList<ActivityDrilldownPlay>> FetchDrilldownPlaysAsync(
        ActivityDrilldownRequest request,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        var (startUtc, endUtc) = WatchStatsPeriodHelper.RangeUtc(
            request.Period,
            request.CustomStartUtc,
            request.CustomEndUtc);

        // Tautulli range filters are `after` / `before` (YYYY-MM-DD).
        // `start_date`/`end_date` are single-day only; unix values return zero rows.
        var after = startUtc.Date.ToString("yyyy-MM-dd");
        var before = endUtc.Date.ToString("yyyy-MM-dd");

        var maxRows = request.Period switch
        {
            WatchStatsPeriod.Today => 80,
            WatchStatsPeriod.Days7 => 200,
            WatchStatsPeriod.Days30 => 500,
            WatchStatsPeriod.All => 1500,
            _ => 800
        };

        var extras = new List<(string Key, string Value)>
        {
            ("after", after),
            ("before", before),
            ("order_column", "date"),
            ("order_dir", "desc")
        };

        if (request.Kind == ActivityDrilldownKind.User)
        {
            if (!string.IsNullOrWhiteSpace(request.Key) && request.Key.All(char.IsDigit))
                extras.Add(("user_id", request.Key));
            else if (!string.IsNullOrWhiteSpace(request.Title))
                extras.Add(("user", request.Title));
            else if (!string.IsNullOrWhiteSpace(request.Key))
                extras.Add(("user", request.Key));
        }
        else if (request.Kind == ActivityDrilldownKind.Media)
        {
            // Prefer exact Plex rating_key when the leaderboard stored it; otherwise search by title.
            // Keys may be prefixed: r:{rating_key} for movies, g:{grandparent_rating_key} for TV.
            // TV shows are often rematched in Plex (new grandparent_rating_key). Top TV points at the
            // current key only — filtering by that key drops viewers of older library copies.
            // Use title search for series when we have a display title so rematches are included.
            if (TryParseRatingKey(request.Key, out var ratingParam, out var ratingValue))
            {
                if (string.Equals(ratingParam, "grandparent_rating_key", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(request.Title))
                {
                    extras.Add(("search", request.Title));
                }
                else
                {
                    extras.Add((ratingParam, ratingValue));
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.Key) && request.Key.All(char.IsDigit))
            {
                extras.Add(("rating_key", request.Key));
            }
            else
            {
                var search = !string.IsNullOrWhiteSpace(request.Title) ? request.Title : request.Key;
                if (!string.IsNullOrWhiteSpace(search))
                    extras.Add(("search", search));
            }
        }
        else if (request.Kind == ActivityDrilldownKind.MediaType)
        {
            var mediaType = NormalizeMediaTypeKey(request.Key, request.Title);
            if (!string.IsNullOrWhiteSpace(mediaType))
                extras.Add(("media_type", mediaType));
        }
        else if (request.Kind == ActivityDrilldownKind.Quality)
        {
            var decision = NormalizeQualityKey(request.Key, request.Title);
            if (!string.IsNullOrWhiteSpace(decision))
                extras.Add(("transcode_decision", decision));
        }
        else if (request.Kind is ActivityDrilldownKind.Genre or ActivityDrilldownKind.Platform)
        {
            // No precise server-side filter — pull a larger window and filter client-side.
            maxRows = Math.Max(maxRows, 1000);
        }

        var since = new DateTimeOffset(DateTime.SpecifyKind(startUtc.Date, DateTimeKind.Utc));
        var history = await FetchHistoryAsync(since, maxRows, ct, extras);
        IEnumerable<ImportedPlayEvent> filtered = history;

        if (request.Kind == ActivityDrilldownKind.User)
        {
            filtered = history.Where(h =>
                MatchesUser(h, request.Key, request.Title));
        }
        else if (request.Kind == ActivityDrilldownKind.Genre)
        {
            var genre = !string.IsNullOrWhiteSpace(request.Key) ? request.Key : request.Title;
            filtered = history.Where(h =>
                h.Genres.Any(g => string.Equals(g, genre, StringComparison.OrdinalIgnoreCase)));
        }
        else if (request.Kind == ActivityDrilldownKind.Platform)
        {
            var platform = !string.IsNullOrWhiteSpace(request.Key) ? request.Key : request.Title;
            filtered = history.Where(h =>
                string.Equals(h.Platform, platform, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.Client, platform, StringComparison.OrdinalIgnoreCase));
        }
        else if (request.Kind == ActivityDrilldownKind.MediaType)
        {
            var mediaType = NormalizeMediaTypeKey(request.Key, request.Title);
            filtered = history.Where(h =>
                string.Equals(h.MediaType, mediaType, StringComparison.OrdinalIgnoreCase));
        }
        else if (request.Kind == ActivityDrilldownKind.Quality)
        {
            filtered = history;
        }
        else if (request.Kind == ActivityDrilldownKind.Media
                 && TryParseRatingKey(request.Key, out var rkParam, out var parsedKey))
        {
            // Movies: tighten to the exact rating_key (episode rows use per-episode ids).
            // TV: title search can return partial matches — keep rows for this series only.
            if (string.Equals(rkParam, "rating_key", StringComparison.Ordinal))
            {
                filtered = history.Where(h =>
                    string.Equals(h.ExternalItemId, parsedKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(h.Title, request.Title, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(h.SeriesTitle, request.Title, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(request.Title))
            {
                filtered = history.Where(h =>
                    string.Equals(h.Title, request.Title, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(h.SeriesTitle, request.Title, StringComparison.OrdinalIgnoreCase)
                    || (h.ItemTitle?.Contains(request.Title, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            else
            {
                filtered = history;
            }
        }
        else if (request.Kind == ActivityDrilldownKind.Media
                 && !string.IsNullOrWhiteSpace(request.Key)
                 && request.Key.All(char.IsDigit))
        {
            filtered = history.Where(h =>
                string.Equals(h.ExternalItemId, request.Key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.Title, request.Title, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.SeriesTitle, request.Title, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            filtered = history.Where(h =>
                string.Equals(h.Title, request.Key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.SeriesTitle, request.Key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.Title, request.Title, StringComparison.OrdinalIgnoreCase)
                || string.Equals(h.SeriesTitle, request.Title, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(request.Title)
                    && (h.Title.Contains(request.Title, StringComparison.OrdinalIgnoreCase)
                        || (h.SeriesTitle?.Contains(request.Title, StringComparison.OrdinalIgnoreCase) ?? false))));
        }

        var endExclusive = DateTime.SpecifyKind(endUtc.Date.AddDays(1), DateTimeKind.Utc);
        filtered = filtered.Where(h => h.PlayedAtUtc >= since && h.PlayedAtUtc < endExclusive);

        return filtered
            .OrderByDescending(h => h.PlayedAtUtc)
            .Take(500)
            .Select(h => new ActivityDrilldownPlay(
                h.Title,
                h.MediaType == "episode" ? h.SeriesTitle : null,
                h.UserDisplayName,
                Math.Round(h.DurationSeconds / 3600.0, 2),
                h.PlayedAtUtc,
                string.IsNullOrWhiteSpace(h.ThumbPath) ? null : PosterUrls.PlexThumb(h.ThumbPath),
                WatchStatsSources.Plex,
                h.MediaType,
                h.Genres,
                h.UserExternalId,
                h.Platform ?? h.Client,
                h.MediaType == "episode" ? h.ItemTitle : null,
                !string.IsNullOrWhiteSpace(h.ExternalItemId) ? $"r:{h.ExternalItemId}" : null))
            .ToList();
    }

    private static string NormalizeMediaTypeKey(string? key, string? title)
    {
        var raw = (key ?? title ?? "").Trim().ToLowerInvariant();
        return raw switch
        {
            "movie" or "movies" => "movie",
            "episode" or "episodes" or "tv" or "television" => "episode",
            "music" or "track" or "tracks" or "audio" => "music",
            _ => raw
        };
    }

    private static string NormalizeQualityKey(string? key, string? title)
    {
        var raw = (key ?? title ?? "").Trim().ToLowerInvariant();
        return raw switch
        {
            "direct play" or "directplay" or "direct_play" => "direct play",
            "direct stream" or "directstream" or "direct_stream" or "copy" => "copy",
            "transcode" or "transcoding" => "transcode",
            _ => raw
        };
    }

    private static bool MatchesUser(ImportedPlayEvent play, string key, string title)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (string.Equals(play.UserDisplayName, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(play.UserExternalId, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            if (string.Equals(play.UserDisplayName, title, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryParseRatingKey(string? key, out string param, out string value)
    {
        param = "";
        value = "";
        if (string.IsNullOrWhiteSpace(key) || key.Length < 3 || key[1] != ':')
            return false;

        var prefix = key[..2];
        var raw = key[2..];
        if (string.IsNullOrWhiteSpace(raw) || !raw.All(char.IsDigit))
            return false;

        if (prefix.Equals("r:", StringComparison.OrdinalIgnoreCase))
        {
            param = "rating_key";
            value = raw;
            return true;
        }

        if (prefix.Equals("g:", StringComparison.OrdinalIgnoreCase))
        {
            param = "grandparent_rating_key";
            value = raw;
            return true;
        }

        return false;
    }

    private ImportedPlayEvent? MapHistoryRow(JsonElement row, IReadOnlyDictionary<string, string>? libraryNames = null)
    {
        var playId = ReadString(row, "reference_id", "id", "row_id");
        if (string.IsNullOrWhiteSpace(playId))
            return null;

        var mediaType = NormalizeMediaType(ReadString(row, "media_type"));
        var itemTitle = ReadString(row, "full_title", "title", "grandparent_title") ?? "Unknown";
        var series = ReadString(row, "grandparent_title", "parent_title");
        var title = itemTitle;
        if (mediaType == "episode" && !string.IsNullOrWhiteSpace(series))
            title = series;

        var genres = ParseGenres(row);
        var duration = ReadInt(row, "duration", "length") ?? 0;
        var playedAt = ReadUnixDate(row, "date", "started", "stopped") ?? DateTimeOffset.UtcNow;
        var grandparentKey = ReadString(row, "grandparent_rating_key");
        var sectionId = ReadString(row, "section_id");
        var transcode = ReadString(row, "transcode_decision", "stream_type");
        string? libraryName = null;
        if (!string.IsNullOrWhiteSpace(sectionId)
            && libraryNames is not null
            && libraryNames.TryGetValue(sectionId, out var mappedName))
            libraryName = mappedName;

        return new ImportedPlayEvent(
            WatchStatsSources.Plex,
            playId,
            ReadString(row, "friendly_name", "user") ?? "Unknown",
            ReadString(row, "user_id"),
            title,
            mediaType == "episode" ? series : null,
            mediaType,
            genres,
            ReadString(row, "player"),
            ReadString(row, "platform"),
            playedAt,
            Math.Max(duration, 0),
            ReadString(row, "rating_key"),
            ReadString(row, "grandparent_thumb", "thumb"),
            mediaType == "episode" ? itemTitle : null,
            TranscodeDecision: transcode,
            LibraryExternalId: sectionId,
            LibraryName: libraryName,
            GrandparentExternalId: grandparentKey);
    }

    private async Task<IReadOnlyList<ActivityChartPoint>> GetCategorySeriesChartAsync(
        string cmd,
        int timeRange,
        CancellationToken ct)
    {
        var doc = await GetApiAsync(cmd, ct,
            ("time_range", timeRange.ToString()),
            ("y_axis", "plays"));

        return doc is null ? [] : ParseCategorySeries(doc.RootElement);
    }

    private async Task<(IReadOnlyList<ActivityChartPoint> Quality, ActivityQualityStats? Stats)> GetStreamQualityAsync(
        int timeRange,
        CancellationToken ct)
    {
        var doc = await GetApiAsync("get_plays_by_stream_type", ct,
            ("time_range", timeRange.ToString()),
            ("y_axis", "plays"));

        return doc is null ? ([], null) : ParseStreamTypeChart(doc.RootElement);
    }

    private static (IReadOnlyList<ActivityChartPoint> Quality, ActivityQualityStats? Stats) ParseStreamTypeChart(JsonElement root)
    {
        if (!root.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
            return ([], null);

        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var series in seriesEl.EnumerateArray())
        {
            var name = ReadString(series, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!series.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
                continue;

            var sum = dataEl.EnumerateArray()
                .Sum(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0);
            if (sum <= 0)
                continue;

            totals[name] = totals.GetValueOrDefault(name) + sum;
        }

        var points = totals
            .Select(kv => new ActivityChartPoint(NormalizeStreamTypeLabel(kv.Key), kv.Value))
            .Where(p => p.Value > 0)
            .ToList();

        if (points.Count == 0)
            return ([], null);

        var directPlay = (int)Math.Round(points.FirstOrDefault(p => p.Label == "Direct play")?.Value ?? 0);
        var directStream = (int)Math.Round(points.FirstOrDefault(p => p.Label == "Direct stream")?.Value ?? 0);
        var transcode = (int)Math.Round(points.FirstOrDefault(p => p.Label == "Transcode")?.Value ?? 0);
        var total = directPlay + directStream + transcode;
        if (total <= 0)
            return (points, null);

        return (points, new ActivityQualityStats(
            directPlay,
            directStream,
            transcode,
            total,
            Percent(directPlay, total),
            Percent(directStream, total),
            Percent(transcode, total)));
    }

    private static IReadOnlyList<ActivityChartPoint> ParseCategorySeries(JsonElement root)
    {
        if (!root.TryGetProperty("categories", out var categoriesEl) || categoriesEl.ValueKind != JsonValueKind.Array)
            return [];
        if (!root.TryGetProperty("series", out var seriesEl) || seriesEl.ValueKind != JsonValueKind.Array)
            return [];

        var categories = categoriesEl.EnumerateArray()
            .Select(c => c.GetString() ?? "")
            .ToList();

        JsonElement? totalSeries = null;
        foreach (var series in seriesEl.EnumerateArray())
        {
            if (string.Equals(ReadString(series, "name"), "Total", StringComparison.OrdinalIgnoreCase))
            {
                totalSeries = series;
                break;
            }
        }

        totalSeries ??= seriesEl.EnumerateArray().FirstOrDefault();
        if (totalSeries is null
            || !totalSeries.Value.TryGetProperty("data", out var dataEl)
            || dataEl.ValueKind != JsonValueKind.Array)
            return [];

        var values = dataEl.EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0)
            .ToList();

        var points = new List<ActivityChartPoint>();
        for (var i = 0; i < Math.Min(categories.Count, values.Count); i++)
        {
            var label = categories[i];
            if (values[i] <= 0 || string.IsNullOrWhiteSpace(label))
                continue;

            points.Add(new ActivityChartPoint(FormatChartLabel(label), values[i]));
        }

        return points;
    }

    private static string FormatChartLabel(string label)
    {
        if (int.TryParse(label, out var hour) && hour is >= 0 and <= 23)
            return hour switch
            {
                0 => "12 AM",
                12 => "12 PM",
                < 12 => $"{hour} AM",
                _ => $"{hour - 12} PM"
            };

        return label;
    }

    private static string NormalizeStreamTypeLabel(string label) => label switch
    {
        "Direct Play" => "Direct play",
        "Direct Stream" => "Direct stream",
        _ when label.Contains("Transcode", StringComparison.OrdinalIgnoreCase) => "Transcode",
        _ => label
    };

    private static int Percent(int value, int total) =>
        total <= 0 ? 0 : (int)Math.Round(value * 100.0 / total);

    private async Task<JsonDocument?> GetApiAsync(string cmd, CancellationToken ct, params (string Key, string Value)[] extra)
    {
        var query = new List<string>
        {
            $"cmd={Uri.EscapeDataString(cmd)}",
            $"apikey={Uri.EscapeDataString(Tautulli.ApiKey)}"
        };
        query.AddRange(extra.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        var url = $"{Tautulli.Url.TrimEnd('/')}/api/v2?{string.Join('&', query)}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            return null;
        if (responseEl.TryGetProperty("result", out var result) && result.GetString() != "success")
            return null;
        if (!responseEl.TryGetProperty("data", out var data))
            return null;

        return JsonDocument.Parse(data.GetRawText());
    }

    private static string? ReadString(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (el.TryGetProperty(name, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                };
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
                return n;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        return null;
    }

    private static DateTimeOffset? ReadUnixDate(JsonElement el, params string[] names)
    {
        var raw = ReadInt(el, names);
        return raw is > 0 ? DateTimeOffset.FromUnixTimeSeconds(raw.Value) : null;
    }

    private static IReadOnlyList<string> ParseGenres(JsonElement row)
    {
        if (row.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
            return genres.EnumerateArray().Select(g => g.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToList();

        var text = ReadString(row, "genres");
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string NormalizeMediaType(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "movie" => "movie",
        "episode" => "episode",
        "track" => "music",
        "music" => "music",
        _ => "other"
    };

    private static IReadOnlyList<WatchStatRow> MapUserRows(JsonElement rows, int limit)
    {
        if (rows.ValueKind != JsonValueKind.Array)
            return [];

        return rows.EnumerateArray()
            .Select(row =>
            {
                var name = ReadString(row, "friendly_name", "user");
                if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "Unknown", StringComparison.OrdinalIgnoreCase))
                    return null;

                var thumb = ReadString(row, "user_thumb");
                return new WatchStatRow(
                    name,
                    $"{ReadInt(row, "total_plays") ?? 0} plays",
                    Math.Round((ReadInt(row, "total_duration") ?? 0) / 3600.0, 1),
                    ReadInt(row, "total_plays") ?? 0,
                    MapUserThumb(thumb),
                    null,
                    WatchStatsSources.Plex,
                    null,
                    ReadString(row, "user_id") ?? name,
                    ActivityDrilldownKind.User);
            })
            .Where(r => r is not null)
            .Cast<WatchStatRow>()
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> MapMediaRows(JsonElement rows, string mediaType, int limit)
    {
        if (rows.ValueKind != JsonValueKind.Array)
            return [];

        return rows.EnumerateArray()
            .Select(row =>
            {
                var title = mediaType == "episode"
                    ? ReadString(row, "grandparent_title", "title") ?? "Unknown"
                    : ReadString(row, "title", "grandchild_title") ?? "Unknown";
                var subtitle = mediaType == "episode"
                    ? ReadString(row, "title", "grandchild_title")
                    : ReadInt(row, "year")?.ToString();
                var thumb = ReadString(row, "thumb", "grandparent_thumb");
                // Prefer rating_key so drill-down can query Tautulli history exactly.
                // Prefix distinguishes movie rating_key vs TV series grandparent_rating_key.
                var ratingKey = mediaType == "episode"
                    ? ReadString(row, "grandparent_rating_key", "rating_key")
                    : ReadString(row, "rating_key");
                string? drilldownKey = null;
                if (!string.IsNullOrWhiteSpace(ratingKey) && ratingKey.All(char.IsDigit))
                    drilldownKey = mediaType == "episode" ? $"g:{ratingKey}" : $"r:{ratingKey}";

                return new WatchStatRow(
                    title,
                    subtitle,
                    Math.Round((ReadInt(row, "total_duration") ?? 0) / 3600.0, 1),
                    ReadInt(row, "total_plays") ?? 0,
                    string.IsNullOrWhiteSpace(thumb) ? null : PosterUrls.PlexThumb(thumb),
                    null,
                    WatchStatsSources.Plex,
                    null,
                    drilldownKey ?? title,
                    ActivityDrilldownKind.Media);
            })
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> MapPlatformRows(JsonElement rows, int limit)
    {
        if (rows.ValueKind != JsonValueKind.Array)
            return [];

        return rows.EnumerateArray()
            .Select(row =>
            {
                var name = ReadString(row, "platform", "platform_name") ?? "Unknown";
                return new WatchStatRow(
                    name,
                    $"{ReadInt(row, "total_plays") ?? 0} plays",
                    Math.Round((ReadInt(row, "total_duration") ?? 0) / 3600.0, 1),
                    ReadInt(row, "total_plays") ?? 0,
                    null,
                    null,
                    WatchStatsSources.Plex,
                    DrilldownKey: name,
                    DrilldownKind: ActivityDrilldownKind.Platform);
            })
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static IReadOnlyList<WatchStatRow> MapRecentRows(JsonElement rows, int limit)
    {
        if (rows.ValueKind != JsonValueKind.Array)
            return [];

        // Tautulli's last_watched stat returns one row per play, not per unique item —
        // group so a replayed title only shows once (its most recent play, list is
        // already newest-first so GroupBy's stable ordering keeps that).
        return rows.EnumerateArray()
            .Select(row =>
            {
                var mediaType = NormalizeMediaType(ReadString(row, "media_type"));
                var title = mediaType == "episode"
                    ? ReadString(row, "grandparent_title", "title") ?? "Unknown"
                    : ReadString(row, "title", "grandchild_title") ?? "Unknown";
                var thumb = ReadString(row, "thumb", "grandparent_thumb");
                var ratingKey = ReadString(row, "rating_key");

                return (
                    Row: new WatchStatRow(
                        title,
                        ReadString(row, "friendly_name", "user"),
                        Math.Round((ReadInt(row, "duration") ?? ReadInt(row, "total_duration") ?? 0) / 3600.0, 1),
                        1,
                        string.IsNullOrWhiteSpace(thumb) ? null : PosterUrls.PlexThumb(thumb),
                        null,
                        WatchStatsSources.Plex,
                        null,
                        title,
                        ActivityDrilldownKind.Media),
                    Key: !string.IsNullOrWhiteSpace(ratingKey) ? $"id:{ratingKey}" : $"t:{title}");
            })
            .GroupBy(x => x.Key)
            .Select(g => g.First().Row)
            .Take(limit)
            .ToList();
    }

    private static DateTimeOffset? ReadLastPlay(JsonElement rows)
    {
        if (rows.ValueKind != JsonValueKind.Array)
            return null;

        var first = rows.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined)
            return null;

        return ReadUnixDate(first, "last_watch", "last_play", "date", "started");
    }

    private static string? MapUserThumb(string? thumb)
    {
        if (string.IsNullOrWhiteSpace(thumb))
            return null;
        if (thumb.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || thumb.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return thumb;
        return PosterUrls.PlexThumb(thumb);
    }
}

public sealed record TautulliActivityCharts(
    IReadOnlyList<ActivityChartPoint> ByDayOfWeek,
    IReadOnlyList<ActivityChartPoint> ByHourOfDay,
    IReadOnlyList<ActivityChartPoint> Quality,
    ActivityQualityStats? QualityStats);

public sealed record ImportedPlayEvent(
    string Source,
    string ExternalPlayId,
    string UserDisplayName,
    string? UserExternalId,
    string Title,
    string? SeriesTitle,
    string MediaType,
    IReadOnlyList<string> Genres,
    string? Client,
    string? Platform,
    DateTimeOffset PlayedAtUtc,
    int DurationSeconds,
    string? ExternalItemId,
    string? ThumbPath,
    // The item's own title (e.g. episode name) before it gets rolled up to the series
    // name in Title/SeriesTitle. Used only for drilldown-play display, not aggregation.
    string? ItemTitle = null,
    string? TranscodeDecision = null,
    string? LibraryName = null,
    string? LibraryExternalId = null,
    double? ProgressPercent = null,
    string? GrandparentExternalId = null);
