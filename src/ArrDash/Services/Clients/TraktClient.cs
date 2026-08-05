using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArrDash.Configuration;

namespace ArrDash.Services.Clients;

public sealed class TraktClient(HttpClient http, MediaServiceOptionsAccessor options, ILogger<TraktClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private TraktOptions Trakt => options.Options.Trakt;

    public bool IsAppConfigured => Trakt.IsConfigured;
    public string ClientId => Trakt.ClientId;
    public string ClientSecret => Trakt.ClientSecret;

    /// <summary>Official PIN page used by media-center apps (trakt.tv/activate).</summary>
    public const string ActivateUrl = "https://trakt.tv/activate";

    /// <summary>Where to create or recover Trakt OAuth Client ID/Secret.</summary>
    public const string ApplicationsUrl = "https://trakt.tv/oauth/applications";

    public async Task<TraktDeviceCodeResponse?> RequestDeviceCodeAsync(CancellationToken ct)
    {
        EnsureAppConfigured();
        using var content = JsonContent(new { client_id = ClientId });
        using var response = await SendUnauthAsync(HttpMethod.Post, "/oauth/device/code", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Contains("invalid_client", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Trakt Client ID/Secret are invalid or the OAuth app was deleted. "
                    + $"Copy a valid pair from {ApplicationsUrl}, paste under Settings → API keys, Save, then try Connect again.");
            }

            throw new InvalidOperationException(
                $"Trakt device code failed ({(int)response.StatusCode}): {Truncate(body, 240)}");
        }

        var device = await ReadJsonAsync<TraktDeviceCodeResponse>(response, ct);
        if (device is not null)
        {
            // Always send users to the documented PIN page — some API responses / typos
            // (e.g. /authorize) 404 with Trakt's "Nothingness. The void." page.
            device.VerificationUrl = ActivateUrl;
        }

        return device;
    }

    public async Task<TraktTokenResponse?> PollDeviceTokenAsync(
        string deviceCode,
        CancellationToken ct)
    {
        EnsureAppConfigured();
        using var content = JsonContent(new
        {
            code = deviceCode,
            client_id = ClientId,
            client_secret = ClientSecret
        });
        using var response = await SendUnauthAsync(HttpMethod.Post, "/oauth/device/token", content, ct);
        var err = response.IsSuccessStatusCode
            ? ""
            : await response.Content.ReadAsStringAsync(ct);

        // Pending / rate-limit — keep polling. Trakt usually returns 400 + JSON error.
        if ((int)response.StatusCode is 400 or 412)
        {
            if (string.IsNullOrWhiteSpace(err)
                || err.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase)
                || err.Contains("slow_down", StringComparison.OrdinalIgnoreCase))
                return null;

            if (err.Contains("expired", StringComparison.OrdinalIgnoreCase)
                || err.Contains("access_denied", StringComparison.OrdinalIgnoreCase)
                || err.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Trakt authorization ended: {Truncate(err, 240)}");

            // Unknown 400 — keep polling rather than aborting the whole connect flow.
            return null;
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Trakt device token failed ({(int)response.StatusCode}): {Truncate(err, 240)}");

        return await ReadJsonAsync<TraktTokenResponse>(response, ct);
    }

    /// <summary>Lightweight credential check that does not start a device-code session.</summary>
    public Task ProbeApiAsync(CancellationToken ct) => ProbeApiAsync(ClientId, ct);

    /// <summary>Probe with an explicit Client ID (e.g. unsaved Settings form values).</summary>
    public async Task ProbeApiAsync(string clientId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Trakt Client ID is required.");

        using var response = await SendUnauthAsync(HttpMethod.Get, "/movies/trending?limit=1", null, ct, clientId);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Trakt API probe failed ({(int)response.StatusCode}): {Truncate(body, 240)}");
        }
    }

    public async Task<TraktTokenResponse?> RefreshTokenAsync(
        string refreshToken,
        CancellationToken ct)
    {
        EnsureAppConfigured();
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException(
                "Trakt refresh token is missing. Disconnect and reconnect the Trakt account in Settings.");

        using var content = JsonContent(new
        {
            refresh_token = refreshToken,
            client_id = ClientId,
            client_secret = ClientSecret,
            grant_type = "refresh_token"
        });
        using var response = await SendUnauthAsync(HttpMethod.Post, "/oauth/token", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Trakt token refresh failed ({(int)response.StatusCode}). "
                + "Disconnect and reconnect the Trakt account in Settings. "
                + Truncate(body, 200));
        }

        return await ReadJsonAsync<TraktTokenResponse>(response, ct);
    }

    public async Task<TraktUserSettings?> GetSettingsAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAuthAsync(HttpMethod.Get, "/users/settings", accessToken, null, ct);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<TraktUserSettings>(response, ct);
    }

    public async Task<TraktLastActivities?> GetLastActivitiesAsync(string accessToken, CancellationToken ct)
    {
        using var response = await SendAuthAsync(HttpMethod.Get, "/sync/last_activities", accessToken, null, ct);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<TraktLastActivities>(response, ct);
    }

    public async Task<IReadOnlyList<TraktHistoryItem>> GetHistoryAsync(
        string accessToken,
        string type,
        DateTimeOffset? startAt,
        CancellationToken ct,
        Action<int, int, int>? onPage = null)
    {
        var results = new List<TraktHistoryItem>();
        var page = 1;
        const int limit = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var query = new StringBuilder($"/sync/history/{type}?page={page}&limit={limit}&extended=full");
            if (startAt is not null)
                query.Append($"&start_at={Uri.EscapeDataString(startAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}");

            using var response = await SendAuthAsync(HttpMethod.Get, query.ToString(), accessToken, null, ct);
            response.EnsureSuccessStatusCode();
            var batch = await ReadJsonAsync<List<TraktHistoryItem>>(response, ct) ?? [];
            results.AddRange(batch);

            var pageCount = 0;
            if (response.Headers.TryGetValues("X-Pagination-Page-Count", out var pageCounts))
                _ = int.TryParse(pageCounts.FirstOrDefault(), out pageCount);

            onPage?.Invoke(page, pageCount, results.Count);

            if (pageCount <= 0 || page >= pageCount || batch.Count == 0)
                break;

            page++;
        }

        return results;
    }

    public async Task<TraktHistoryAddResult?> AddToHistoryAsync(
        string accessToken,
        object body,
        CancellationToken ct)
    {
        using var content = JsonContent(body);
        using var response = await SendAuthAsync(HttpMethod.Post, "/sync/history", accessToken, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Trakt history add failed ({Status}): {Body}", (int)response.StatusCode, Truncate(err, 240));
            return null;
        }

        return await ReadJsonAsync<TraktHistoryAddResult>(response, ct);
    }

    /// <summary>GET /sync/collection/movies or /sync/collection/shows (paged).</summary>
    public async Task<IReadOnlyList<TraktCollectionItem>> GetCollectionAsync(
        string accessToken,
        string type,
        CancellationToken ct,
        Action<int, int, int>? onPage = null)
    {
        var results = new List<TraktCollectionItem>();
        var page = 1;
        const int limit = 100;
        var pathType = type.Trim().ToLowerInvariant() switch
        {
            "show" or "shows" => "shows",
            _ => "movies"
        };

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var path = $"/sync/collection/{pathType}?page={page}&limit={limit}";
            using var response = await SendAuthAsync(HttpMethod.Get, path, accessToken, null, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("Trakt collection get failed ({Status}): {Body}", (int)response.StatusCode, Truncate(err, 240));
                break;
            }

            var batch = await ReadJsonAsync<List<TraktCollectionItem>>(response, ct) ?? [];
            results.AddRange(batch);

            var pageCount = 0;
            if (response.Headers.TryGetValues("X-Pagination-Page-Count", out var pageCounts))
                _ = int.TryParse(pageCounts.FirstOrDefault(), out pageCount);

            onPage?.Invoke(page, pageCount, results.Count);

            if (pageCount <= 0 || page >= pageCount || batch.Count == 0)
                break;

            page++;
        }

        return results;
    }

    public async Task<TraktCollectionAddResult?> AddToCollectionAsync(
        string accessToken,
        object body,
        CancellationToken ct)
    {
        using var content = JsonContent(body);
        using var response = await SendAuthAsync(HttpMethod.Post, "/sync/collection", accessToken, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Trakt collection add failed ({Status}): {Body}", (int)response.StatusCode, Truncate(err, 240));
            return null;
        }

        return await ReadJsonAsync<TraktCollectionAddResult>(response, ct);
    }

    /// <summary>POST /scrobble/stop — progress 0–100. Prefer ≥80 for completed plays.</summary>
    public async Task<TraktScrobbleResult?> ScrobbleStopAsync(
        string accessToken,
        object body,
        CancellationToken ct)
    {
        using var content = JsonContent(body);
        using var response = await SendAuthAsync(HttpMethod.Post, "/scrobble/stop", accessToken, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            // 409 = already scrobbled recently — treat as soft success for webhook idempotency.
            if ((int)response.StatusCode == 409)
            {
                logger.LogDebug("Trakt scrobble stop conflict (already recorded): {Body}", Truncate(err, 200));
                return new TraktScrobbleResult { Action = "conflict" };
            }

            logger.LogWarning("Trakt scrobble stop failed ({Status}): {Body}", (int)response.StatusCode, Truncate(err, 240));
            return null;
        }

        return await ReadJsonAsync<TraktScrobbleResult>(response, ct);
    }

    private void EnsureAppConfigured()
    {
        if (!IsAppConfigured)
            throw new InvalidOperationException("Trakt client ID is not configured in Settings → API keys.");
    }

    private async Task<HttpResponseMessage> SendUnauthAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken ct,
        string? apiKeyOverride = null)
    {
        var request = new HttpRequestMessage(method, "https://api.trakt.tv" + path) { Content = content };
        ApplyCommonHeaders(request, apiKeyOverride);
        return await http.SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendAuthAsync(
        HttpMethod method,
        string path,
        string accessToken,
        HttpContent? content,
        CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, "https://api.trakt.tv" + path) { Content = content };
        ApplyCommonHeaders(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await http.SendAsync(request, ct);
        if ((int)response.StatusCode == 429)
        {
            var delay = TimeSpan.FromSeconds(2);
            if (response.Headers.RetryAfter?.Delta is TimeSpan d)
                delay = d;
            await Task.Delay(delay, ct);
            response.Dispose();
            request = new HttpRequestMessage(method, "https://api.trakt.tv" + path) { Content = content };
            ApplyCommonHeaders(request);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            response = await http.SendAsync(request, ct);
        }

        return response;
    }

    private void ApplyCommonHeaders(HttpRequestMessage request, string? apiKeyOverride = null)
    {
        // Cloudflare blocks Trakt API calls without an identifying User-Agent.
        request.Headers.TryAddWithoutValidation("User-Agent", "ArrDash/1.0");
        request.Headers.TryAddWithoutValidation("trakt-api-version", "2");
        request.Headers.TryAddWithoutValidation("trakt-api-key", apiKeyOverride ?? ClientId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(empty body)";
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}

public sealed class TraktDeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_url")] public string VerificationUrl { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
}

public sealed class TraktTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("created_at")] public long? CreatedAt { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
}

public sealed class TraktUserSettings
{
    [JsonPropertyName("user")] public TraktUser? User { get; set; }
}

public sealed class TraktUser
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class TraktLastActivities
{
    [JsonPropertyName("all")] public DateTimeOffset? All { get; set; }
    [JsonPropertyName("movies")] public TraktActivityBucket? Movies { get; set; }
    [JsonPropertyName("episodes")] public TraktActivityBucket? Episodes { get; set; }
}

public sealed class TraktActivityBucket
{
    [JsonPropertyName("watched_at")] public DateTimeOffset? WatchedAt { get; set; }
}

public sealed class TraktHistoryItem
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("watched_at")] public DateTimeOffset WatchedAt { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("movie")] public TraktMovie? Movie { get; set; }
    [JsonPropertyName("episode")] public TraktEpisode? Episode { get; set; }
    [JsonPropertyName("show")] public TraktShow? Show { get; set; }
}

public sealed class TraktMovie
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("ids")] public TraktIds? Ids { get; set; }
    [JsonPropertyName("runtime")] public int? Runtime { get; set; }
}

public sealed class TraktShow
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("year")] public int? Year { get; set; }
    [JsonPropertyName("ids")] public TraktIds? Ids { get; set; }
}

public sealed class TraktEpisode
{
    [JsonPropertyName("season")] public int Season { get; set; }
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("ids")] public TraktIds? Ids { get; set; }
    [JsonPropertyName("runtime")] public int? Runtime { get; set; }
}

public sealed class TraktIds
{
    [JsonPropertyName("trakt")] public int? Trakt { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("imdb")] public string? Imdb { get; set; }
    [JsonPropertyName("tmdb")] public int? Tmdb { get; set; }
    [JsonPropertyName("tvdb")] public int? Tvdb { get; set; }
}

public sealed class TraktHistoryAddResult
{
    [JsonPropertyName("added")] public TraktHistoryCounts? Added { get; set; }
    [JsonPropertyName("not_found")] public TraktHistoryNotFound? NotFound { get; set; }
}

public sealed class TraktHistoryCounts
{
    [JsonPropertyName("movies")] public int Movies { get; set; }
    [JsonPropertyName("episodes")] public int Episodes { get; set; }
}

public sealed class TraktHistoryNotFound
{
    [JsonPropertyName("movies")] public List<JsonElement>? Movies { get; set; }
    [JsonPropertyName("episodes")] public List<JsonElement>? Episodes { get; set; }
    [JsonPropertyName("shows")] public List<JsonElement>? Shows { get; set; }
}

public sealed class TraktCollectionItem
{
    [JsonPropertyName("collected_at")] public DateTimeOffset? CollectedAt { get; set; }
    [JsonPropertyName("movie")] public TraktMovie? Movie { get; set; }
    [JsonPropertyName("show")] public TraktShow? Show { get; set; }
    [JsonPropertyName("episodes")] public List<TraktCollectedEpisode>? Episodes { get; set; }
}

public sealed class TraktCollectedEpisode
{
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("collected_at")] public DateTimeOffset? CollectedAt { get; set; }
    [JsonPropertyName("ids")] public TraktIds? Ids { get; set; }
}

public sealed class TraktCollectionAddResult
{
    [JsonPropertyName("added")] public TraktCollectionCounts? Added { get; set; }
    [JsonPropertyName("existing")] public TraktCollectionCounts? Existing { get; set; }
    [JsonPropertyName("not_found")] public TraktHistoryNotFound? NotFound { get; set; }
}

public sealed class TraktCollectionCounts
{
    [JsonPropertyName("movies")] public int Movies { get; set; }
    [JsonPropertyName("episodes")] public int Episodes { get; set; }
    [JsonPropertyName("shows")] public int? Shows { get; set; }
}

public sealed class TraktScrobbleResult
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("progress")] public double? Progress { get; set; }
    [JsonPropertyName("movie")] public TraktMovie? Movie { get; set; }
    [JsonPropertyName("episode")] public TraktEpisode? Episode { get; set; }
    [JsonPropertyName("show")] public TraktShow? Show { get; set; }
}
