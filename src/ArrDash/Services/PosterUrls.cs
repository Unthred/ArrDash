namespace ArrDash.Services;

public static class PosterUrls
{
    public static string Sonarr(int seriesId) => $"/api/poster/sonarr/{seriesId}";

    public static string Radarr(int movieId) => $"/api/poster/radarr/{movieId}";

    public static string Lidarr(int artistId) => $"/api/poster/lidarr/{artistId}";

    public static string ChaptarrBook(int bookId) => $"/api/poster/chaptarr/book/{bookId}";

    public static string ChaptarrAuthor(int authorId) => $"/api/poster/chaptarr/author/{authorId}";

    public static string AudiobookShelf(string itemId) => $"/api/poster/audiobookshelf/{itemId}";

    public static string PlexThumb(string thumbPath) =>
        $"/api/thumbnail/plex?path={Uri.EscapeDataString(thumbPath)}";

    public static string EmbyItem(string itemId) => $"/api/thumbnail/emby/{itemId}";

    public static string JellyfinItem(string itemId) => $"/api/thumbnail/jellyfin/{itemId}";

    /// <summary>Poster resolved by provider ids/title for events with no native artwork (Trakt).</summary>
    public static string Media(string mediaType, int? tmdbId, string? imdbId, string? title, int? year)
    {
        var parts = new List<string> { $"type={Uri.EscapeDataString(mediaType)}" };
        if (tmdbId is not null)
            parts.Add($"tmdb={tmdbId}");
        if (!string.IsNullOrWhiteSpace(imdbId))
            parts.Add($"imdb={Uri.EscapeDataString(imdbId)}");
        if (!string.IsNullOrWhiteSpace(title))
            parts.Add($"title={Uri.EscapeDataString(title)}");
        if (year is not null)
            parts.Add($"year={year}");
        return $"/api/poster/media?{string.Join('&', parts)}";
    }
}
