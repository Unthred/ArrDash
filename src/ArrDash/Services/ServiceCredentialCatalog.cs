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
        ("sonarr", "Sonarr", "In Sonarr: Settings → General → Security → copy the API key.", "https://wiki.servarr.com/sonarr/settings#security"),
        ("radarr", "Radarr", "In Radarr: Settings → General → Security → copy the API key.", "https://wiki.servarr.com/radarr/settings#security"),
        ("lidarr", "Lidarr", "In Lidarr: Settings → General → Security → copy the API key.", "https://wiki.servarr.com/lidarr/settings#security"),
        ("chaptarr", "Chaptarr", "In Chaptarr: Settings → General → Security → copy the API key.", "https://github.com/Chaptarr/Chaptarr"),
        ("audiobookshelf", "AudioBookShelf", "In AudioBookShelf: Settings → Users → your user → API keys (or Admin → API keys). Paste the Bearer token.", "https://www.audiobookshelf.org/docs"),
        ("slskd", "slskd", "In slskd: Settings → copy the API key (if API auth is enabled).", "https://github.com/slskd/slskd"),
        ("plex", "Plex", "Sign in to Plex → Settings → Account → Authorized devices, or use a token from plex.tv/account. Paste the X-Plex-Token value.", "https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/"),
        ("emby", "Emby", "In Emby: Dashboard → Advanced → API Keys → create or copy an existing key.", "https://emby.media/support/articles/API-Keys.html")
    ];
}
