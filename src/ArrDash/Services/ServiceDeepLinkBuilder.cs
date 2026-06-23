using ArrDash.Models;

namespace ArrDash.Services;

public static class ServiceDeepLinkBuilder
{
    public static string? BuildItemUrl(MediaSource source, string? serviceBaseUrl, string? titleSlug)
    {
        if (string.IsNullOrWhiteSpace(serviceBaseUrl))
            return null;

        var root = serviceBaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(titleSlug))
            return root;

        var path = source switch
        {
            MediaSource.Sonarr => $"series/{titleSlug}",
            MediaSource.Radarr => $"movie/{titleSlug}",
            MediaSource.Lidarr => $"artist/{titleSlug}",
            MediaSource.Chaptarr => $"book/{titleSlug}",
            _ => null
        };

        return path is null ? root : $"{root}/{path}";
    }

    public static string? BuildAudiobookShelfItemUrl(string? serviceBaseUrl, string itemId) =>
        string.IsNullOrWhiteSpace(serviceBaseUrl) || string.IsNullOrWhiteSpace(itemId)
            ? null
            : $"{serviceBaseUrl.TrimEnd('/')}/item/{itemId}";
}
