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
}
