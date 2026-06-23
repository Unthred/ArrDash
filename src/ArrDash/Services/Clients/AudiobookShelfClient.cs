using System.Text.Json;
using ArrDash.Configuration;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Services.Clients;

public sealed class AudiobookShelfClient(HttpClient http, MediaServiceOptionsAccessor options)
{
    private ServiceEndpoint Abs => options.Options.AudiobookShelf;

    public bool IsConfigured => Abs.IsConfigured;

    public async Task<(IReadOnlyList<DownloadItem> Items, ServiceHealth Health)> FetchRecentAsync(
        int limit,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("audiobookshelf", "AudioBookShelf", false, false, null, null));

        try
        {
            var libraries = await GetLibrariesAsync(ct);
            if (libraries.Count == 0)
                return ([], new ServiceHealth("audiobookshelf", "AudioBookShelf", true, true, null, null));

            var fetchSize = Math.Clamp(limit * 2, limit, 100);
            var tasks = libraries.Select(lib => FetchLibraryItemsAsync(lib.Id, fetchSize, ct));
            var batches = await Task.WhenAll(tasks);

            var items = batches
                .SelectMany(x => x)
                .OrderByDescending(x => x.Timestamp)
                .Take(limit)
                .ToList();

            var version = await GetVersionAsync(ct);
            return (items, new ServiceHealth("audiobookshelf", "AudioBookShelf", true, true, null, version));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("audiobookshelf", "AudioBookShelf", true, false, ex.Message, null));
        }
    }

    public async Task<LibraryStatItem?> FetchLibraryStatsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var libraries = await GetLibrariesAsync(ct);
            if (libraries.Count == 0)
                return null;

            var bookCount = 0L;
            foreach (var library in libraries)
            {
                var total = await FetchLibraryItemCountAsync(library.Id, ct);
                if (total > 0)
                    bookCount += total;
            }

            if (bookCount <= 0)
                return null;

            var headline = bookCount == 1 ? "1 book in library" : $"{CountDisplayHelper.Format(bookCount)} books in library";
            return new LibraryStatItem(
                "audiobookshelf",
                "AudioBookShelf",
                headline,
                null,
                "#00d2be",
                Abs.Url.TrimEnd('/'),
                bookCount);
        }
        catch
        {
            return null;
        }
    }

    private async Task<long> FetchLibraryItemCountAsync(string libraryId, CancellationToken ct)
    {
        using var response = await SendAsync($"/api/libraries/{libraryId}/items?limit=1", ct);
        if (!response.IsSuccessStatusCode)
            return 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        return doc.RootElement.TryGetProperty("total", out var total) ? total.GetInt64() : 0;
    }

    private async Task<IReadOnlyList<(string Id, string Name, string MediaType)>> GetLibrariesAsync(CancellationToken ct)
    {
        using var response = await SendAsync("/api/libraries", ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("libraries", out var libraries) ||
            libraries.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<(string, string, string)>();
        foreach (var library in libraries.EnumerateArray())
        {
            if (!library.TryGetProperty("id", out var idEl) ||
                !library.TryGetProperty("mediaType", out var mediaTypeEl))
                continue;

            if (!string.Equals(mediaTypeEl.GetString(), "book", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = idEl.GetString();
            var name = library.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "Library" : "Library";
            if (!string.IsNullOrWhiteSpace(id))
                results.Add((id, name, "book"));
        }

        return results;
    }

    private async Task<IReadOnlyList<DownloadItem>> FetchLibraryItemsAsync(
        string libraryId,
        int limit,
        CancellationToken ct)
    {
        var path = $"/api/libraries/{libraryId}/items?limit={limit}&sort=addedAt&desc=1&minified=1";
        using var response = await SendAsync(path, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
            return [];

        var items = new List<DownloadItem>();
        foreach (var result in results.EnumerateArray())
        {
            var mapped = MapItem(result);
            if (mapped is not null)
                items.Add(mapped);
        }

        return items;
    }

    private DownloadItem? MapItem(JsonElement result)
    {
        if (!result.TryGetProperty("id", out var idEl))
            return null;

        var id = idEl.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (!result.TryGetProperty("media", out var media) ||
            !media.TryGetProperty("metadata", out var metadata))
            return null;

        var title = metadata.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var author = metadata.TryGetProperty("authorName", out var authorEl) ? authorEl.GetString() : null;
        var asin = metadata.TryGetProperty("asin", out var asinEl) ? asinEl.GetString() : null;
        var addedAt = result.TryGetProperty("addedAt", out var addedEl) && addedEl.TryGetInt64(out var addedMs)
            ? DateTimeOffset.FromUnixTimeMilliseconds(addedMs)
            : DateTimeOffset.UtcNow;

        var absUrl = ServiceDeepLinkBuilder.BuildAudiobookShelfItemUrl(Abs.Url, id);

        return new DownloadItem(
            $"abs-{id}",
            MediaSource.AudiobookShelf,
            MediaType.Audiobook,
            title,
            author,
            DownloadEvent.Imported,
            addedAt,
            PosterUrls.AudiobookShelf(id),
            null,
            null,
            Asin: asin,
            ExternalUrl: absUrl,
            AudiobookShelfUrl: absUrl);
    }

    private async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync("/api/status", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.TryGetProperty("serverVersion", out var version)
                ? version.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken ct)
    {
        var url = $"{Abs.Url.TrimEnd('/')}{path}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Abs.ApiKey);
        return await http.SendAsync(request, ct);
    }
}
