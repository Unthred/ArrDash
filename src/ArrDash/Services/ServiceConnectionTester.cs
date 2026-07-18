using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using ArrDash.Configuration;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public sealed record ServiceTestInput(string? Url, string? ApiKeyOrToken);

public sealed class ServiceConnectionTester(
    IHttpClientFactory httpClientFactory,
    MediaServiceOptionsAccessor optionsAccessor,
    TautulliClient tautulli,
    TracearrClient tracearr,
    TraktClient trakt,
    ILogger<ServiceConnectionTester> logger)
{
    public Task<(bool Ok, string Message)> TestAsync(string serviceKey, CancellationToken ct) =>
        TestAsync(serviceKey, null, ct);

    public async Task<(bool Ok, string Message)> TestAsync(string serviceKey, ServiceTestInput? input, CancellationToken ct)
    {
        try
        {
            var options = optionsAccessor.Options;
            return serviceKey.ToLowerInvariant() switch
            {
                "sonarr" => await TestArr(options.Sonarr, input, "Sonarr", "v3", ct),
                "radarr" => await TestArr(options.Radarr, input, "Radarr", "v3", ct),
                "lidarr" => await TestArr(options.Lidarr, input, "Lidarr", "v1", ct),
                "chaptarr" => await TestArr(options.Chaptarr, input, "Chaptarr", "v1", ct),
                "audiobookshelf" => await TestAudiobookShelf(options.AudiobookShelf, input, ct),
                "plex" => await TestPlex(options.Plex, input, ct),
                "emby" => await TestEmbyLike(options.Emby, input, "Emby", ct),
                "jellyfin" => await TestEmbyLike(options.Jellyfin, input, "Jellyfin", ct),
                "tautulli" => await TestTautulli(input, ct),
                "tracearr" => await TestTracearr(input, ct),
                "trakt" => await TestTraktAsync(options.Trakt, input, ct),
                "tmdb" => await TestTmdbAsync(options.Tmdb, input, ct),
                "slskd" => TestSlskd(ResolveEndpoint(options.Slskd, input), "slskd"),
                _ => (false, "Unknown service")
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TestAsync failed");
            return (false, ex.Message);
        }
    }

    private async Task<(bool Ok, string Message)> TestTraktAsync(TraktOptions current, ServiceTestInput? input, CancellationToken ct)
    {
        var clientId = FirstNonEmpty(input?.ApiKeyOrToken, current.ClientId);
        if (string.IsNullOrWhiteSpace(clientId))
            return (false, "Trakt Client ID required");
        if (string.IsNullOrWhiteSpace(current.ClientSecret) && string.IsNullOrWhiteSpace(input?.ApiKeyOrToken))
            return (false, "Save Client ID and Client Secret, then connect an account on the Watch stats tab");
        if (string.IsNullOrWhiteSpace(current.ClientSecret))
            return (true, "Client ID present — save Client Secret to finish app setup");

        // Live probe: validates Client ID + headers without starting a device-code session
        // (device codes would invalidate an in-progress Connect).
        try
        {
            await trakt.ProbeApiAsync(ct);
            return (true, "Trakt API reachable — use Connect on Watch stats to authorize with PIN");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TestTraktAsync failed");
            return (false, ex.Message);
        }
    }

    private async Task<(bool Ok, string Message)> TestTmdbAsync(TmdbOptions current, ServiceTestInput? input, CancellationToken ct)
    {
        var apiKey = FirstNonEmpty(input?.ApiKeyOrToken, current.ApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "TMDB API key required");

        var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
        using var response = await client.GetAsync(
            $"https://api.themoviedb.org/3/configuration?api_key={Uri.EscapeDataString(apiKey)}", ct);
        return response.IsSuccessStatusCode
            ? (true, "Connected")
            : (false, $"HTTP {(int)response.StatusCode}");
    }

    private static ServiceEndpoint ResolveEndpoint(ServiceEndpoint current, ServiceTestInput? input) =>
        new()
        {
            Url = FirstNonEmpty(input?.Url, current.Url),
            ApiKey = FirstNonEmpty(input?.ApiKeyOrToken, current.ApiKey)
        };

    private static PlexOptions ResolvePlex(PlexOptions current, ServiceTestInput? input) =>
        new()
        {
            Url = FirstNonEmpty(input?.Url, current.Url),
            Token = FirstNonEmpty(input?.ApiKeyOrToken, current.Token)
        };

    private static string FirstNonEmpty(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private async Task<(bool Ok, string Message)> TestArr(
        ServiceEndpoint current,
        ServiceTestInput? input,
        string name,
        string apiVersion,
        CancellationToken ct)
    {
        var endpoint = ResolveEndpoint(current, input);
        if (string.IsNullOrWhiteSpace(endpoint.Url) || string.IsNullOrWhiteSpace(endpoint.ApiKey))
            return (false, $"{name} URL or API key required");

        var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{endpoint.Url.TrimEnd('/')}/api/{apiVersion}/history?pageSize=1&sortKey=date&sortDirection=descending");
        request.Headers.Add("X-Api-Key", endpoint.ApiKey);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (false, $"HTTP {(int)response.StatusCode}");

        var version = await TryGetArrVersionAsync(client, endpoint, apiVersion, logger, ct);
        return version is not null ? (true, $"Connected (v{version})") : (true, "Connected");
    }

    private static async Task<string?> TryGetArrVersionAsync(
        HttpClient client,
        ServiceEndpoint endpoint,
        string apiVersion,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.Url.TrimEnd('/')}/api/{apiVersion}/system/status");
            request.Headers.Add("X-Api-Key", endpoint.ApiKey);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch (Exception ex) {
            logger.LogWarning(ex, "TryGetArrVersionAsync failed");
            return null;
        }
    }

    private async Task<(bool Ok, string Message)> TestAudiobookShelf(
        ServiceEndpoint current,
        ServiceTestInput? input,
        CancellationToken ct)
    {
        var endpoint = ResolveEndpoint(current, input);
        if (string.IsNullOrWhiteSpace(endpoint.Url) || string.IsNullOrWhiteSpace(endpoint.ApiKey))
            return (false, "AudioBookShelf URL or API key required");

        var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.Url.TrimEnd('/')}/api/libraries");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        using var response = await client.SendAsync(request, ct);
        return response.IsSuccessStatusCode
            ? (true, "Connected")
            : (false, $"HTTP {(int)response.StatusCode}");
    }

    private async Task<(bool Ok, string Message)> TestPlex(PlexOptions current, ServiceTestInput? input, CancellationToken ct)
    {
        var plex = ResolvePlex(current, input);
        if (string.IsNullOrWhiteSpace(plex.Url) || string.IsNullOrWhiteSpace(plex.Token))
            return (false, "Plex URL or token required");

        var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
        var url = $"{plex.Url.TrimEnd('/')}/status/sessions?X-Plex-Token={Uri.EscapeDataString(plex.Token)}";
        using var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode
            ? (true, "Connected")
            : (false, $"HTTP {(int)response.StatusCode}");
    }

    private async Task<(bool Ok, string Message)> TestEmbyLike(
        ServiceEndpoint current,
        ServiceTestInput? input,
        string name,
        CancellationToken ct)
    {
        var endpoint = ResolveEndpoint(current, input);
        if (string.IsNullOrWhiteSpace(endpoint.Url) || string.IsNullOrWhiteSpace(endpoint.ApiKey))
            return (false, $"{name} URL or API key required");

        var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
        var url = $"{endpoint.Url.TrimEnd('/')}/System/Info/Public?api_key={Uri.EscapeDataString(endpoint.ApiKey)}";
        using var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode
            ? (true, "Connected")
            : (false, $"HTTP {(int)response.StatusCode}");
    }

    private async Task<(bool Ok, string Message)> TestTautulli(ServiceTestInput? input, CancellationToken ct)
    {
        if (input?.Url is not null || input?.ApiKeyOrToken is not null)
        {
            var endpoint = ResolveEndpoint(optionsAccessor.Options.Tautulli, input);
            if (string.IsNullOrWhiteSpace(endpoint.Url) || string.IsNullOrWhiteSpace(endpoint.ApiKey))
                return (false, "Tautulli URL or API key required");

            var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
            var url = $"{endpoint.Url.TrimEnd('/')}/api/v2?apikey={Uri.EscapeDataString(endpoint.ApiKey)}&cmd=get_server_info";
            using var response = await client.GetAsync(url, ct);
            return response.IsSuccessStatusCode ? (true, "Connected") : (false, $"HTTP {(int)response.StatusCode}");
        }

        return await tautulli.TestConnectionAsync(ct);
    }

    private async Task<(bool Ok, string Message)> TestTracearr(ServiceTestInput? input, CancellationToken ct)
    {
        if (input?.Url is not null || input?.ApiKeyOrToken is not null)
        {
            var endpoint = ResolveEndpoint(optionsAccessor.Options.Tracearr, input);
            if (string.IsNullOrWhiteSpace(endpoint.Url) || string.IsNullOrWhiteSpace(endpoint.ApiKey))
                return (false, "Tracearr URL or API key required");

            var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.Url.TrimEnd('/')}/api/v1/public/health");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey.Trim());
            using var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? (true, "Connected") : (false, $"HTTP {(int)response.StatusCode}");
        }

        return await tracearr.TestConnectionAsync(ct);
    }

    private static (bool Ok, string Message) TestSlskd(ServiceEndpoint endpoint, string name) =>
        endpoint.IsConfigured ? (true, "Configured") : (false, $"{name} URL or API key required");
}
