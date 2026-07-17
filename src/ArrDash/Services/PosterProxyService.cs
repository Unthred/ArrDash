using System.Text.Json;
using ArrDash.Configuration;
using ArrDash.Services;

namespace ArrDash.Services;

public sealed class PosterProxyService(IHttpClientFactory httpClientFactory, MediaServiceOptionsAccessor options)
{
    public async Task<IResult> FetchAsync(string source, int id, string? kind, CancellationToken ct)
    {
        if (string.Equals(source, "chaptarr", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(kind, "book", StringComparison.OrdinalIgnoreCase))
        {
            return await FetchChaptarrBookCoverAsync(id, ct);
        }

        var (baseUrl, apiKey, path) = Resolve(source, id, kind);
        if (baseUrl is null || apiKey is null || path is null)
            return Results.NotFound();

        var url = $"{baseUrl.TrimEnd('/')}{path}?apikey={apiKey}";
        return await FetchImageAsync(url, ct);
    }

    public Task<IResult> FetchChaptarrBookCoverAsync(int bookId, CancellationToken ct) =>
        FetchChaptarrCoverFromApiAsync($"/api/v1/book/{bookId}", ct);

    public async Task<IResult> FetchStreamingThumbnailAsync(string source, string pathOrItemId, CancellationToken ct)
    {
        var services = options.Options;
        return source.ToLowerInvariant() switch
        {
            "plex" when IsSafeAbsolutePath(pathOrItemId) => await FetchPlexThumbnailAsync(pathOrItemId, ct),
            "tracearr" when services.Tracearr.IsConfigured && IsSafeAbsolutePath(pathOrItemId) =>
                await FetchTracearrImageAsync($"{services.Tracearr.Url.TrimEnd('/')}{pathOrItemId}", ct),
            "emby" when services.Emby.IsConfigured && !string.IsNullOrWhiteSpace(pathOrItemId) =>
                await FetchImageAsync(
                    $"{services.Emby.Url.TrimEnd('/')}/Items/{Uri.EscapeDataString(pathOrItemId)}/Images/Primary?api_key={Uri.EscapeDataString(services.Emby.ApiKey)}",
                    ct),
            "jellyfin" when services.Jellyfin.IsConfigured && !string.IsNullOrWhiteSpace(pathOrItemId) =>
                await FetchImageAsync(
                    $"{services.Jellyfin.Url.TrimEnd('/')}/Items/{Uri.EscapeDataString(pathOrItemId)}/Images/Primary?api_key={Uri.EscapeDataString(services.Jellyfin.ApiKey)}",
                    ct),
            _ => Results.NotFound()
        };
    }

    private async Task<IResult> FetchPlexThumbnailAsync(string thumbPath, CancellationToken ct)
    {
        var services = options.Options;
        if (services.Plex.IsConfigured)
        {
            var directUrl =
                $"{services.Plex.Url.TrimEnd('/')}{thumbPath}?X-Plex-Token={Uri.EscapeDataString(services.Plex.Token)}";
            return await FetchImageAsync(directUrl, ct);
        }

        if (!services.Tautulli.IsConfigured)
            return Results.NotFound();

        return await FetchTautulliPmsImageAsync(thumbPath, ct);
    }

    private async Task<IResult> FetchTautulliPmsImageAsync(string imgPath, CancellationToken ct)
    {
        var tautulli = options.Options.Tautulli;
        var query = new List<string>
        {
            "cmd=pms_image_proxy",
            $"apikey={Uri.EscapeDataString(tautulli.ApiKey)}",
            $"img={Uri.EscapeDataString(imgPath)}",
            "width=300",
            "height=450"
        };

        var ratingKey = TryParsePlexRatingKey(imgPath);
        if (!string.IsNullOrWhiteSpace(ratingKey))
            query.Add($"rating_key={Uri.EscapeDataString(ratingKey)}");

        var url = $"{tautulli.Url.TrimEnd('/')}/api/v2?{string.Join('&', query)}";
        return await FetchImageAsync(url, ct);
    }

    private static string? TryParsePlexRatingKey(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && string.Equals(parts[0], "library", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "metadata", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[2], out _))
        {
            return parts[2];
        }

        return null;
    }

    private async Task<IResult> FetchTracearrImageAsync(string url, CancellationToken ct)
    {
        var token = options.Options.Tracearr.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return Results.NotFound();

        var client = httpClientFactory.CreateClient(nameof(PosterProxyService));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Results.NotFound();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Results.Bytes(bytes, contentType);
    }

    public async Task<IResult> FetchAudiobookShelfCoverAsync(string itemId, CancellationToken ct)
    {
        var abs = options.Options.AudiobookShelf;
        if (!abs.IsConfigured || string.IsNullOrWhiteSpace(itemId))
            return Results.NotFound();

        var url = $"{abs.Url.TrimEnd('/')}/api/items/{Uri.EscapeDataString(itemId)}/cover";
        var client = httpClientFactory.CreateClient(nameof(PosterProxyService));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", abs.ApiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Results.NotFound();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Results.Bytes(bytes, contentType);
    }

    private async Task<IResult> FetchChaptarrCoverFromApiAsync(string bookPath, CancellationToken ct)
    {
        var chaptarr = options.Options.Chaptarr;
        if (!chaptarr.IsConfigured)
            return Results.NotFound();

        var client = httpClientFactory.CreateClient(nameof(PosterProxyService));
        var bookUrl = $"{chaptarr.Url.TrimEnd('/')}{bookPath}";
        using var bookRequest = new HttpRequestMessage(HttpMethod.Get, bookUrl);
        bookRequest.Headers.Add("X-Api-Key", chaptarr.ApiKey);
        using var bookResponse = await client.SendAsync(bookRequest, ct);
        if (!bookResponse.IsSuccessStatusCode)
            return Results.NotFound();

        await using var stream = await bookResponse.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var remoteUrl = SelectImageUrl(root, "cover")
            ?? SelectImageUrl(root, "poster")
            ?? (root.TryGetProperty("author", out var author) ? SelectImageUrl(author, "poster") : null);

        if (string.IsNullOrWhiteSpace(remoteUrl))
            return Results.NotFound();

        return await FetchImageAsync(remoteUrl, ct);
    }

    private static string? SelectImageUrl(JsonElement container, string coverType)
    {
        if (!container.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
            return null;

        string? fallback = null;
        foreach (var image in images.EnumerateArray())
        {
            if (!image.TryGetProperty("url", out var urlEl))
                continue;

            var url = urlEl.GetString();
            if (string.IsNullOrWhiteSpace(url))
                continue;

            fallback ??= url;
            var type = image.TryGetProperty("coverType", out var typeEl) ? typeEl.GetString() : null;
            if (string.Equals(type, coverType, StringComparison.OrdinalIgnoreCase))
                return url;
        }

        return fallback;
    }

    private async Task<IResult> FetchImageAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(PosterProxyService));
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Results.NotFound();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return Results.Bytes(bytes, contentType);
    }

    private static bool IsSafeAbsolutePath(string path) =>
        path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal);

    private (string? BaseUrl, string? ApiKey, string? Path) Resolve(string source, int id, string? kind)
    {
        var services = options.Options;
        return source.ToLowerInvariant() switch
        {
            "sonarr" when services.Sonarr.IsConfigured =>
                (services.Sonarr.Url, services.Sonarr.ApiKey, $"/MediaCover/{id}/poster.jpg"),
            "radarr" when services.Radarr.IsConfigured =>
                (services.Radarr.Url, services.Radarr.ApiKey, $"/MediaCover/{id}/poster.jpg"),
            "lidarr" when services.Lidarr.IsConfigured =>
                (services.Lidarr.Url, services.Lidarr.ApiKey, $"/MediaCover/Artist/{id}/poster.jpg"),
            "chaptarr" when services.Chaptarr.IsConfigured && kind == "author" =>
                (services.Chaptarr.Url, services.Chaptarr.ApiKey, $"/MediaCover/Author/{id}/poster.jpg"),
            _ => (null, null, null)
        };
    }
}
