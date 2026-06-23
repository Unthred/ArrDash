using ArrDash.Models;

namespace ArrDash.Services;

public static class ServiceCredentialCatalog
{
    public static IReadOnlyList<CredentialStatus> BuildStatuses(
        IReadOnlyDictionary<string, bool> configured,
        IReadOnlyDictionary<string, string?> hints,
        IReadOnlyDictionary<string, string?> urls) =>
        Definitions.Select(d => new CredentialStatus(
            d.Key,
            d.Label,
            configured.GetValueOrDefault(d.Key),
            hints.GetValueOrDefault(d.Key),
            d.Instructions,
            d.HelpUrl,
            urls.GetValueOrDefault(d.Key))).ToList();

    private static readonly (string Key, string Label, string Instructions, string HelpUrl)[] Definitions =
    [
        ("sonarr", "Sonarr", "In Sonarr: Settings → General → Security → copy the API key.", "https://sonarr.yeradonkey.com/settings/general"),
        ("radarr", "Radarr", "In Radarr: Settings → General → Security → copy the API key.", "https://radarr.yeradonkey.com/settings/general"),
        ("lidarr", "Lidarr", "In Lidarr: Settings → General → Security → copy the API key.", "https://lidarr.yeradonkey.com/settings/general"),
        ("chaptarr", "Chaptarr", "In Chaptarr: Settings → General → Security → copy the API key.", "https://chaptarr.yeradonkey.com/settings/general"),
        ("audiobookshelf", "AudioBookShelf", "In AudioBookShelf: Settings → Users → your user → API keys (or Admin → API keys). Paste the Bearer token.", "https://audiobooks.yeradonkey.com/config/users"),
        ("slskd", "slskd", "In slskd: Settings → copy the API key (if API auth is enabled).", "https://soulseek.yeradonkey.com/"),
        ("plex", "Plex", "Sign in to Plex → Settings → Account → Authorized devices, or use a token from plex.tv/account. Paste the X-Plex-Token value.", "https://video.yeradonkey.com/web/index.html"),
        ("emby", "Emby", "In Emby: Dashboard → Advanced → API Keys → create or copy an existing key.", "https://emby.yeradonkey.com/web/index.html")
    ];
}
