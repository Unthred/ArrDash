using ArrDash.Models;

namespace ArrDash.Services;

public static class MediaQuickLinkBuilder
{
    public static IReadOnlyList<MediaQuickLink> Build(DownloadItem item)
    {
        if (item.MediaType == MediaType.Audiobook)
            return BuildAudiobookLinks(item);

        var links = new List<MediaQuickLink>();

        if (!string.IsNullOrWhiteSpace(item.ImdbId))
        {
            links.Add(new MediaQuickLink(
                "IMDb",
                $"https://www.imdb.com/title/{item.ImdbId.Trim()}/",
                "imdb"));
        }

        if (item.TmdbId is int tmdbId and > 0)
        {
            var segment = item.MediaType == MediaType.Tv ? "tv" : "movie";
            links.Add(new MediaQuickLink(
                "TMDb",
                $"https://www.themoviedb.org/{segment}/{tmdbId}",
                "tmdb"));
        }

        if (item.TvdbId is int tvdbId and > 0 && item.MediaType == MediaType.Tv)
        {
            links.Add(new MediaQuickLink(
                "TVDb",
                $"https://www.thetvdb.com/dereferrer/series/{tvdbId}",
                "tvdb"));
        }

        if (!string.IsNullOrWhiteSpace(item.YouTubeTrailerId))
        {
            links.Add(new MediaQuickLink(
                "Trailer",
                $"https://www.youtube.com/watch?v={item.YouTubeTrailerId.Trim()}",
                "youtube"));
        }

        if (BuildAppLink(item) is { } appLink)
            links.Add(appLink);

        return links;
    }

    private static IReadOnlyList<MediaQuickLink> BuildAudiobookLinks(DownloadItem item)
    {
        var links = new List<MediaQuickLink>();

        if (!string.IsNullOrWhiteSpace(item.GoodreadsUrl))
            links.Add(new MediaQuickLink("Goodreads", item.GoodreadsUrl, "goodreads"));

        if (!string.IsNullOrWhiteSpace(item.HardcoverUrl))
            links.Add(new MediaQuickLink("Hardcover", item.HardcoverUrl, "hardcover"));

        if (!string.IsNullOrWhiteSpace(item.AudiobookShelfUrl))
            links.Add(new MediaQuickLink("ABS", item.AudiobookShelfUrl, "abs"));

        if (!string.IsNullOrWhiteSpace(item.ChaptarrUrl))
            links.Add(new MediaQuickLink("Chaptarr", item.ChaptarrUrl, "chaptarr"));

        return links;
    }

    private static MediaQuickLink? BuildAppLink(DownloadItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ExternalUrl))
            return null;

        return item.Source switch
        {
            MediaSource.Sonarr => new MediaQuickLink("Sonarr", item.ExternalUrl, "sonarr"),
            MediaSource.Radarr => new MediaQuickLink("Radarr", item.ExternalUrl, "radarr"),
            MediaSource.Lidarr => new MediaQuickLink("Lidarr", item.ExternalUrl, "lidarr"),
            MediaSource.Chaptarr => new MediaQuickLink("Chaptarr", item.ExternalUrl, "chaptarr"),
            MediaSource.AudiobookShelf => new MediaQuickLink("ABS", item.ExternalUrl, "abs"),
            _ => new MediaQuickLink("Open", item.ExternalUrl, "app")
        };
    }
}
