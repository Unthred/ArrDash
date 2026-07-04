namespace ArrDash.Services;

public static class ContainerNetworkMapper
{
    private static readonly (string Pattern, string Key, string Label)[] Rules =
    [
        ("plex", "plex", "Plex"),
        ("emby", "emby", "Emby"),
        ("jellyfin", "jellyfin", "Jellyfin"),
        ("slskd", "slskd", "slskd"),
        ("audiobookshelf", "audiobookshelf", "AudioBookShelf"),
        ("chaptarr", "chaptarr", "Chaptarr"),
        ("sonarr", "sonarr", "Sonarr"),
        ("radarr", "radarr", "Radarr"),
        ("lidarr", "lidarr", "Lidarr"),
        ("readarr", "readarr", "Readarr"),
        ("qbittorrent", "qbittorrent", "qBittorrent"),
        ("nzbget", "nzbget", "NZBGet"),
        ("sabnzbd", "sabnzbd", "SABnzbd"),
        ("prowlarr", "prowlarr", "Prowlarr"),
        ("transmission", "transmission", "Transmission"),
        ("deluge", "deluge", "Deluge"),
    ];

    public static (string Key, string Label) Map(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            return ("unknown", "Unknown");

        var lower = containerName.ToLowerInvariant();
        foreach (var (pattern, key, label) in Rules)
        {
            if (lower.Contains(pattern, StringComparison.Ordinal))
                return (key, label);
        }

        return (lower, containerName);
    }

    public static bool IsStreamingKey(string key) =>
        key is "plex" or "emby" or "jellyfin";
}
