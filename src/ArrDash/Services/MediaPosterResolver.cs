using System.Text.Json;
using ArrDash.Configuration;

namespace ArrDash.Services;

/// <summary>
/// Resolves posters for play events that carry no native artwork (Trakt history) by provider ids
/// or title: matched in the Emby/Jellyfin library and/or fetched from TMDB, per the Trakt poster
/// mode setting (#37). Resolved (and failed) lookups are cached to disk so each title is resolved
/// against the external services at most once.
/// </summary>
public sealed class MediaPosterResolver(
    IHttpClientFactory httpClientFactory,
    MediaServiceOptionsAccessor options,
    LayoutPreferencesService prefs,
    PosterProxyService proxy,
    IWebHostEnvironment env,
    ILogger<MediaPosterResolver> logger)
{
    private const string NotFound = "";
    // Served when posters are off or unresolvable so <img> tags never show a broken icon.
    private static readonly byte[] TransparentPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private readonly string _cachePath = Path.Combine(
        Environment.GetEnvironmentVariable("ARRDASH_CONFIG_PATH") ?? Path.Combine(env.ContentRootPath, "config"),
        "cache",
        "media-poster-urls.json");
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private Dictionary<string, string>? _cache;

    public async Task<IResult> ServeAsync(
        string? mediaType,
        int? tmdbId,
        string? imdbId,
        string? title,
        int? year,
        CancellationToken ct)
    {
        var mode = NormalizeMode(prefs.Current.TraktPosterMode);
        if (mode == "off")
            return Blank();

        var type = string.Equals(mediaType, "episode", StringComparison.OrdinalIgnoreCase) ? "episode" : "movie";
        if (tmdbId is null && string.IsNullOrWhiteSpace(imdbId) && string.IsNullOrWhiteSpace(title))
            return Blank();

        var url = await ResolveAsync(mode, type, tmdbId, imdbId, title, year, ct);
        return string.IsNullOrWhiteSpace(url)
            ? Blank()
            : await proxy.FetchImageAsync(url, ct);
    }

    private async Task<string?> ResolveAsync(
        string mode,
        string type,
        int? tmdbId,
        string? imdbId,
        string? title,
        int? year,
        CancellationToken ct)
    {
        var identity = BuildIdentity(type, tmdbId, imdbId, title, year);

        if (mode is "library" or "both")
        {
            var url = await CachedAsync($"lib:{identity}",
                () => ResolveFromLibraryAsync(type, tmdbId, imdbId, title, year, ct));
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        if (mode is "tmdb" or "both")
        {
            var url = await CachedAsync($"tmdb:{identity}",
                () => ResolveFromTmdbAsync(type, tmdbId, imdbId, title, year, ct));
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private async Task<string?> ResolveFromLibraryAsync(
        string type,
        int? tmdbId,
        string? imdbId,
        string? title,
        int? year,
        CancellationToken ct)
    {
        foreach (var server in new[] { options.Options.Emby, options.Options.Jellyfin })
        {
            if (!server.IsConfigured)
                continue;

            var itemId = await FindLibraryItemAsync(server, type, tmdbId, imdbId, title, year, ct);
            if (!string.IsNullOrWhiteSpace(itemId))
                return $"{server.Url.TrimEnd('/')}/Items/{Uri.EscapeDataString(itemId)}/Images/Primary" +
                       $"?maxWidth=300&api_key={Uri.EscapeDataString(server.ApiKey)}";
        }

        return null;
    }

    private async Task<string?> FindLibraryItemAsync(
        ServiceEndpoint server,
        string type,
        int? tmdbId,
        string? imdbId,
        string? title,
        int? year,
        CancellationToken ct)
    {
        // Movies match the item itself; episodes match the series (posters live on the series).
        var itemTypes = type == "movie" ? "Movie" : "Series";

        var providerIds = new List<string>();
        if (tmdbId is not null)
            providerIds.Add($"tmdb.{tmdbId}");
        if (!string.IsNullOrWhiteSpace(imdbId))
            providerIds.Add($"imdb.{imdbId}");

        if (providerIds.Count > 0)
        {
            var byId = await QueryFirstItemIdAsync(server, ct,
                ("Recursive", "true"),
                ("IncludeItemTypes", itemTypes),
                ("AnyProviderIdEquals", string.Join(',', providerIds)),
                ("Limit", "1"));
            if (byId is not null)
                return byId;
        }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        var query = new List<(string, string)>
        {
            ("Recursive", "true"),
            ("IncludeItemTypes", itemTypes),
            ("SearchTerm", title),
            ("Limit", "1")
        };
        if (type == "movie" && year is not null)
            query.Add(("Years", year.Value.ToString()));

        return await QueryFirstItemIdAsync(server, ct, query.ToArray());
    }

    private async Task<string?> QueryFirstItemIdAsync(
        ServiceEndpoint server,
        CancellationToken ct,
        params (string Key, string Value)[] query)
    {
        try
        {
            var parts = query
                .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}")
                .Append($"api_key={Uri.EscapeDataString(server.ApiKey)}");
            var url = $"{server.Url.TrimEnd('/')}/Items?{string.Join('&', parts)}";

            var client = httpClientFactory.CreateClient(nameof(MediaPosterResolver));
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("Items", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() == 0)
                return null;

            var first = items[0];
            return first.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ResolveFromTmdbAsync(
        string type,
        int? tmdbId,
        string? imdbId,
        string? title,
        int? year,
        CancellationToken ct)
    {
        var apiKey = options.Options.Tmdb.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        string? posterPath = null;

        if (type == "movie")
        {
            if (tmdbId is not null)
                posterPath = await TmdbPosterAsync($"movie/{tmdbId}", apiKey, "poster_path", ct);
            if (posterPath is null && !string.IsNullOrWhiteSpace(imdbId))
                posterPath = await TmdbFindPosterAsync(imdbId, apiKey, "movie_results", ct);
            if (posterPath is null && !string.IsNullOrWhiteSpace(title))
                posterPath = await TmdbSearchPosterAsync("movie", title, year, apiKey, ct);
        }
        else
        {
            // Trakt episode events may carry episode-level ids; the series title is the
            // reliable handle, with a series-level tmdb id as a cheap first try.
            if (tmdbId is not null)
                posterPath = await TmdbPosterAsync($"tv/{tmdbId}", apiKey, "poster_path", ct);
            if (posterPath is null && !string.IsNullOrWhiteSpace(title))
                posterPath = await TmdbSearchPosterAsync("tv", title, year, apiKey, ct)
                    ?? await TmdbSearchPosterAsync("tv", title, null, apiKey, ct);
        }

        return posterPath is null ? null : $"https://image.tmdb.org/t/p/w342{posterPath}";
    }

    private async Task<string?> TmdbPosterAsync(string path, string apiKey, string property, CancellationToken ct)
    {
        using var doc = await TmdbGetAsync($"{path}?api_key={Uri.EscapeDataString(apiKey)}", ct);
        if (doc is null)
            return null;
        return doc.RootElement.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    private async Task<string?> TmdbFindPosterAsync(string imdbId, string apiKey, string resultsProperty, CancellationToken ct)
    {
        using var doc = await TmdbGetAsync(
            $"find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id&api_key={Uri.EscapeDataString(apiKey)}", ct);
        return doc is null ? null : FirstPoster(doc.RootElement, resultsProperty);
    }

    private async Task<string?> TmdbSearchPosterAsync(string kind, string title, int? year, string apiKey, CancellationToken ct)
    {
        var yearParam = year is null
            ? ""
            : kind == "movie" ? $"&year={year}" : $"&first_air_date_year={year}";
        using var doc = await TmdbGetAsync(
            $"search/{kind}?query={Uri.EscapeDataString(title)}{yearParam}&api_key={Uri.EscapeDataString(apiKey)}", ct);
        return doc is null ? null : FirstPoster(doc.RootElement, "results");
    }

    private static string? FirstPoster(JsonElement root, string resultsProperty)
    {
        if (!root.TryGetProperty(resultsProperty, out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
            return null;

        return results[0].TryGetProperty("poster_path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    private async Task<JsonDocument?> TmdbGetAsync(string pathAndQuery, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(nameof(MediaPosterResolver));
            using var response = await client.GetAsync($"https://api.themoviedb.org/3/{pathAndQuery}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> CachedAsync(string key, Func<Task<string?>> resolve)
    {
        var cache = await LoadCacheAsync();
        lock (cache)
        {
            if (cache.TryGetValue(key, out var hit))
                return hit == NotFound ? null : hit;
        }

        var resolved = await resolve();
        lock (cache)
            cache[key] = resolved ?? NotFound;
        await SaveCacheAsync();
        return resolved;
    }

    private static IResult Blank() => Results.Bytes(TransparentPng, "image/png");

    private static string BuildIdentity(string type, int? tmdbId, string? imdbId, string? title, int? year)
    {
        // Episode events carry episode-level ids; keying on the series title caches one
        // lookup per series instead of one per episode.
        if (type == "episode" && !string.IsNullOrWhiteSpace(title))
            return $"{type}:t:{title.Trim().ToLowerInvariant()}:{year}";
        if (tmdbId is not null)
            return $"{type}:tmdb:{tmdbId}";
        if (!string.IsNullOrWhiteSpace(imdbId))
            return $"{type}:imdb:{imdbId.Trim()}";
        return $"{type}:t:{title?.Trim().ToLowerInvariant()}:{year}";
    }

    private static string NormalizeMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "off" => "off",
        "tmdb" => "tmdb",
        "library" => "library",
        _ => "both"
    };

    private async Task<Dictionary<string, string>> LoadCacheAsync()
    {
        if (_cache is not null)
            return _cache;

        await _ioLock.WaitAsync();
        try
        {
            if (_cache is not null)
                return _cache;

            if (File.Exists(_cachePath))
            {
                await using var stream = File.OpenRead(_cachePath);
                _cache = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream)
                         ?? new Dictionary<string, string>();
            }
            else
            {
                _cache = new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not load poster URL cache");
            _cache = new Dictionary<string, string>();
        }
        finally
        {
            _ioLock.Release();
        }

        return _cache;
    }

    private async Task SaveCacheAsync()
    {
        var cache = _cache;
        if (cache is null)
            return;

        await _ioLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            string json;
            lock (cache)
                json = JsonSerializer.Serialize(cache);
            await File.WriteAllTextAsync(_cachePath, json);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not save poster URL cache");
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
