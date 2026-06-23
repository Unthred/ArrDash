using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using ArrDash.Configuration;

namespace ArrDash.Services;

public sealed record ServiceTestInput(string? Url, string? ApiKeyOrToken);

public sealed class ServiceConnectionTester(
    IHttpClientFactory httpClientFactory,
    MediaServiceOptionsAccessor optionsAccessor)
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
                "emby" => await TestEmby(options.Emby, input, ct),
                "slskd" => TestSlskd(ResolveEndpoint(options.Slskd, input), "slskd"),
                _ => (false, "Unknown service")
            };
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
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

        var version = await TryGetArrVersionAsync(client, endpoint, apiVersion, ct);
        return version is not null ? (true, $"Connected (v{version})") : (true, "Connected");
    }

    private static async Task<string?> TryGetArrVersionAsync(
        HttpClient client,
        ServiceEndpoint endpoint,
        string apiVersion,
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
        catch
        {
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

    private async Task<(bool Ok, string Message)> TestEmby(
        ServiceEndpoint current,
        ServiceTestInput? input,
        CancellationToken ct)
    {
        var endpoint = ResolveEndpoint(current, input);
        if (string.IsNullOrWhiteSpace(endpoint.Url) || string.IsNullOrWhiteSpace(endpoint.ApiKey))
            return (false, "Emby URL or API key required");

        var client = httpClientFactory.CreateClient(nameof(ServiceConnectionTester));
        var url = $"{endpoint.Url.TrimEnd('/')}/System/Info/Public?api_key={Uri.EscapeDataString(endpoint.ApiKey)}";
        using var response = await client.GetAsync(url, ct);
        return response.IsSuccessStatusCode
            ? (true, "Connected")
            : (false, $"HTTP {(int)response.StatusCode}");
    }

    private static (bool Ok, string Message) TestSlskd(ServiceEndpoint endpoint, string name) =>
        endpoint.IsConfigured ? (true, "Configured") : (false, $"{name} URL or API key required");
}
