using System.Text.Json;
using ArrDash.Configuration;

namespace ArrDash.Services;

public sealed class ServiceSecretsStore(IWebHostEnvironment env, ILogger<ServiceSecretsStore> logger)
{
    private readonly string _path = Path.Combine(
        Environment.GetEnvironmentVariable("ARRDASH_CONFIG_PATH") ?? Path.Combine(env.ContentRootPath, "config"),
        "service-secrets.json");
    private readonly object _lock = new();
    private ServiceSecretsFile _secrets = new();

    public event Action? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);

            if (!File.Exists(_path))
                return;

            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<ServiceSecretsFile>(stream, cancellationToken: ct);
            if (loaded is not null)
            {
                lock (_lock)
                    _secrets = loaded;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load service secrets");
        }
    }

    public void ApplyTo(MediaServiceOptions options)
    {
        lock (_lock)
        {
            ApplyIfSet(_secrets.SonarrApiKey, v => options.Sonarr.ApiKey = v);
            ApplyIfSet(_secrets.RadarrApiKey, v => options.Radarr.ApiKey = v);
            ApplyIfSet(_secrets.LidarrApiKey, v => options.Lidarr.ApiKey = v);
            ApplyIfSet(_secrets.ChaptarrApiKey, v => options.Chaptarr.ApiKey = v);
            ApplyIfSet(_secrets.AudiobookShelfApiKey, v => options.AudiobookShelf.ApiKey = v);
            ApplyIfSet(_secrets.SlskdApiKey, v => options.Slskd.ApiKey = v);
            ApplyIfSet(_secrets.PlexToken, v => options.Plex.Token = v);
            ApplyIfSet(_secrets.EmbyApiKey, v => options.Emby.ApiKey = v);
            ApplyIfSet(_secrets.JellyfinApiKey, v => options.Jellyfin.ApiKey = v);
            ApplyIfSet(_secrets.TautulliApiKey, v => options.Tautulli.ApiKey = v);
            ApplyIfSet(_secrets.TracearrApiKey, v => options.Tracearr.ApiKey = v);
            ApplyIfSet(_secrets.SonarrUrl, v => options.Sonarr.Url = v);
            ApplyIfSet(_secrets.RadarrUrl, v => options.Radarr.Url = v);
            ApplyIfSet(_secrets.LidarrUrl, v => options.Lidarr.Url = v);
            ApplyIfSet(_secrets.ChaptarrUrl, v => options.Chaptarr.Url = v);
            ApplyIfSet(_secrets.AudiobookShelfUrl, v => options.AudiobookShelf.Url = v);
            ApplyIfSet(_secrets.SlskdUrl, v => options.Slskd.Url = v);
            ApplyIfSet(_secrets.PlexUrl, v => options.Plex.Url = v);
            ApplyIfSet(_secrets.EmbyUrl, v => options.Emby.Url = v);
            ApplyIfSet(_secrets.JellyfinUrl, v => options.Jellyfin.Url = v);
            ApplyIfSet(_secrets.TautulliUrl, v => options.Tautulli.Url = v);
            ApplyIfSet(_secrets.TracearrUrl, v => options.Tracearr.Url = v);
        }
    }

    public IReadOnlyDictionary<string, string?> GetCurrentUrls(MediaServiceOptions options) =>
        new Dictionary<string, string?>
        {
            ["sonarr"] = FirstUrl(_secrets.SonarrUrl, options.Sonarr.Url),
            ["radarr"] = FirstUrl(_secrets.RadarrUrl, options.Radarr.Url),
            ["lidarr"] = FirstUrl(_secrets.LidarrUrl, options.Lidarr.Url),
            ["chaptarr"] = FirstUrl(_secrets.ChaptarrUrl, options.Chaptarr.Url),
            ["audiobookshelf"] = FirstUrl(_secrets.AudiobookShelfUrl, options.AudiobookShelf.Url),
            ["slskd"] = FirstUrl(_secrets.SlskdUrl, options.Slskd.Url),
            ["plex"] = FirstUrl(_secrets.PlexUrl, options.Plex.Url),
            ["emby"] = FirstUrl(_secrets.EmbyUrl, options.Emby.Url),
            ["jellyfin"] = FirstUrl(_secrets.JellyfinUrl, options.Jellyfin.Url),
            ["tautulli"] = FirstUrl(_secrets.TautulliUrl, options.Tautulli.Url),
            ["tracearr"] = FirstUrl(_secrets.TracearrUrl, options.Tracearr.Url),
        };

    public IReadOnlyDictionary<string, bool> GetConfiguredFlags(MediaServiceOptions options) =>
        new Dictionary<string, bool>
        {
            ["sonarr"] = HasSecret(nameof(ServiceSecretsFile.SonarrApiKey)) || !string.IsNullOrWhiteSpace(options.Sonarr.ApiKey),
            ["radarr"] = HasSecret(nameof(ServiceSecretsFile.RadarrApiKey)) || !string.IsNullOrWhiteSpace(options.Radarr.ApiKey),
            ["lidarr"] = HasSecret(nameof(ServiceSecretsFile.LidarrApiKey)) || !string.IsNullOrWhiteSpace(options.Lidarr.ApiKey),
            ["chaptarr"] = HasSecret(nameof(ServiceSecretsFile.ChaptarrApiKey)) || !string.IsNullOrWhiteSpace(options.Chaptarr.ApiKey),
            ["audiobookshelf"] = HasSecret(nameof(ServiceSecretsFile.AudiobookShelfApiKey)) || !string.IsNullOrWhiteSpace(options.AudiobookShelf.ApiKey),
            ["slskd"] = HasSecret(nameof(ServiceSecretsFile.SlskdApiKey)) || !string.IsNullOrWhiteSpace(options.Slskd.ApiKey),
            ["plex"] = HasSecret(nameof(ServiceSecretsFile.PlexToken)) || !string.IsNullOrWhiteSpace(options.Plex.Token),
            ["emby"] = HasSecret(nameof(ServiceSecretsFile.EmbyApiKey)) || !string.IsNullOrWhiteSpace(options.Emby.ApiKey),
            ["jellyfin"] = HasSecret(nameof(ServiceSecretsFile.JellyfinApiKey)) || !string.IsNullOrWhiteSpace(options.Jellyfin.ApiKey),
            ["tautulli"] = HasSecret(nameof(ServiceSecretsFile.TautulliApiKey)) || !string.IsNullOrWhiteSpace(options.Tautulli.ApiKey),
            ["tracearr"] = HasSecret(nameof(ServiceSecretsFile.TracearrApiKey)) || !string.IsNullOrWhiteSpace(options.Tracearr.ApiKey),
        };

    public IReadOnlyDictionary<string, string?> GetMaskedHints()
    {
        lock (_lock)
        {
            return new Dictionary<string, string?>
            {
                ["sonarr"] = Mask(_secrets.SonarrApiKey),
                ["radarr"] = Mask(_secrets.RadarrApiKey),
                ["lidarr"] = Mask(_secrets.LidarrApiKey),
                ["chaptarr"] = Mask(_secrets.ChaptarrApiKey),
                ["audiobookshelf"] = Mask(_secrets.AudiobookShelfApiKey),
                ["slskd"] = Mask(_secrets.SlskdApiKey),
                ["plex"] = Mask(_secrets.PlexToken),
                ["emby"] = Mask(_secrets.EmbyApiKey),
                ["jellyfin"] = Mask(_secrets.JellyfinApiKey),
                ["tautulli"] = Mask(_secrets.TautulliApiKey),
                ["tracearr"] = Mask(_secrets.TracearrApiKey),
            };
        }
    }

    public async Task SavePartialAsync(ServiceSecretsFile updates, CancellationToken ct = default)
    {
        lock (_lock)
        {
            MergeSecret(updates.SonarrApiKey, v => _secrets.SonarrApiKey = v);
            MergeSecret(updates.RadarrApiKey, v => _secrets.RadarrApiKey = v);
            MergeSecret(updates.LidarrApiKey, v => _secrets.LidarrApiKey = v);
            MergeSecret(updates.ChaptarrApiKey, v => _secrets.ChaptarrApiKey = v);
            MergeSecret(updates.AudiobookShelfApiKey, v => _secrets.AudiobookShelfApiKey = v);
            MergeSecret(updates.SlskdApiKey, v => _secrets.SlskdApiKey = v);
            MergeSecret(updates.PlexToken, v => _secrets.PlexToken = v);
            MergeSecret(updates.EmbyApiKey, v => _secrets.EmbyApiKey = v);
            MergeSecret(updates.JellyfinApiKey, v => _secrets.JellyfinApiKey = v);
            MergeSecret(updates.TautulliApiKey, v => _secrets.TautulliApiKey = v);
            MergeSecret(updates.TracearrApiKey, v => _secrets.TracearrApiKey = v);
            MergeUrl(updates.SonarrUrl, v => _secrets.SonarrUrl = v);
            MergeUrl(updates.RadarrUrl, v => _secrets.RadarrUrl = v);
            MergeUrl(updates.LidarrUrl, v => _secrets.LidarrUrl = v);
            MergeUrl(updates.ChaptarrUrl, v => _secrets.ChaptarrUrl = v);
            MergeUrl(updates.AudiobookShelfUrl, v => _secrets.AudiobookShelfUrl = v);
            MergeUrl(updates.SlskdUrl, v => _secrets.SlskdUrl = v);
            MergeUrl(updates.PlexUrl, v => _secrets.PlexUrl = v);
            MergeUrl(updates.EmbyUrl, v => _secrets.EmbyUrl = v);
            MergeUrl(updates.JellyfinUrl, v => _secrets.JellyfinUrl = v);
            MergeUrl(updates.TautulliUrl, v => _secrets.TautulliUrl = v);
            MergeUrl(updates.TracearrUrl, v => _secrets.TracearrUrl = v);
        }

        await PersistAsync(ct);
        Changed?.Invoke();
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        ServiceSecretsFile snapshot;
        lock (_lock)
            snapshot = Clone(_secrets);

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, snapshot, new JsonSerializerOptions { WriteIndented = true }, ct);

        try
        {
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not set service-secrets.json permissions");
        }
    }

    private bool HasSecret(string name) => name switch
    {
        nameof(ServiceSecretsFile.SonarrApiKey) => !string.IsNullOrWhiteSpace(_secrets.SonarrApiKey),
        nameof(ServiceSecretsFile.RadarrApiKey) => !string.IsNullOrWhiteSpace(_secrets.RadarrApiKey),
        nameof(ServiceSecretsFile.LidarrApiKey) => !string.IsNullOrWhiteSpace(_secrets.LidarrApiKey),
        nameof(ServiceSecretsFile.ChaptarrApiKey) => !string.IsNullOrWhiteSpace(_secrets.ChaptarrApiKey),
        nameof(ServiceSecretsFile.AudiobookShelfApiKey) => !string.IsNullOrWhiteSpace(_secrets.AudiobookShelfApiKey),
        nameof(ServiceSecretsFile.SlskdApiKey) => !string.IsNullOrWhiteSpace(_secrets.SlskdApiKey),
        nameof(ServiceSecretsFile.PlexToken) => !string.IsNullOrWhiteSpace(_secrets.PlexToken),
        nameof(ServiceSecretsFile.EmbyApiKey) => !string.IsNullOrWhiteSpace(_secrets.EmbyApiKey),
        nameof(ServiceSecretsFile.JellyfinApiKey) => !string.IsNullOrWhiteSpace(_secrets.JellyfinApiKey),
        nameof(ServiceSecretsFile.TautulliApiKey) => !string.IsNullOrWhiteSpace(_secrets.TautulliApiKey),
        nameof(ServiceSecretsFile.TracearrApiKey) => !string.IsNullOrWhiteSpace(_secrets.TracearrApiKey),
        _ => false
    };

    private static void ApplyIfSet(string? value, Action<string> apply)
    {
        if (!string.IsNullOrWhiteSpace(value))
            apply(value);
    }

    private static void MergeSecret(string? incoming, Action<string> set)
    {
        if (!string.IsNullOrWhiteSpace(incoming))
            set(incoming.Trim());
    }

    private static void MergeUrl(string? incoming, Action<string> set)
    {
        if (!string.IsNullOrWhiteSpace(incoming))
            set(incoming.Trim());
    }

    private static string? FirstUrl(string? secretUrl, string configuredUrl) =>
        !string.IsNullOrWhiteSpace(secretUrl) ? secretUrl : configuredUrl;

    private static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Length <= 4 ? "****" : $"…{value[^4..]}";
    }

    private static ServiceSecretsFile Clone(ServiceSecretsFile s) => new()
    {
        SonarrApiKey = s.SonarrApiKey,
        RadarrApiKey = s.RadarrApiKey,
        LidarrApiKey = s.LidarrApiKey,
        ChaptarrApiKey = s.ChaptarrApiKey,
        AudiobookShelfApiKey = s.AudiobookShelfApiKey,
        SlskdApiKey = s.SlskdApiKey,
        PlexToken = s.PlexToken,
        EmbyApiKey = s.EmbyApiKey,
        JellyfinApiKey = s.JellyfinApiKey,
        TautulliApiKey = s.TautulliApiKey,
        TracearrApiKey = s.TracearrApiKey,
        SonarrUrl = s.SonarrUrl,
        RadarrUrl = s.RadarrUrl,
        LidarrUrl = s.LidarrUrl,
        ChaptarrUrl = s.ChaptarrUrl,
        AudiobookShelfUrl = s.AudiobookShelfUrl,
        SlskdUrl = s.SlskdUrl,
        PlexUrl = s.PlexUrl,
        EmbyUrl = s.EmbyUrl,
        JellyfinUrl = s.JellyfinUrl,
        TautulliUrl = s.TautulliUrl,
        TracearrUrl = s.TracearrUrl
    };
}

public sealed class ServiceSecretsFile
{
    public string? SonarrApiKey { get; set; }
    public string? RadarrApiKey { get; set; }
    public string? LidarrApiKey { get; set; }
    public string? ChaptarrApiKey { get; set; }
    public string? AudiobookShelfApiKey { get; set; }
    public string? SlskdApiKey { get; set; }
    public string? PlexToken { get; set; }
    public string? EmbyApiKey { get; set; }
    public string? JellyfinApiKey { get; set; }
    public string? TautulliApiKey { get; set; }
    public string? TracearrApiKey { get; set; }
    public string? SonarrUrl { get; set; }
    public string? RadarrUrl { get; set; }
    public string? LidarrUrl { get; set; }
    public string? ChaptarrUrl { get; set; }
    public string? AudiobookShelfUrl { get; set; }
    public string? SlskdUrl { get; set; }
    public string? PlexUrl { get; set; }
    public string? EmbyUrl { get; set; }
    public string? JellyfinUrl { get; set; }
    public string? TautulliUrl { get; set; }
    public string? TracearrUrl { get; set; }
}
