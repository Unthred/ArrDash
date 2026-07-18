using ArrDash.Configuration;

namespace ArrDash.Services;

public sealed class ServiceCredentialsPreviewService
{
    private ServiceSecretsFile? _overlay;

    public event Action? Changed;

    public void Set(ServiceSecretsFile? overlay)
    {
        _overlay = overlay;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_overlay is null) return;
        _overlay = null;
        Changed?.Invoke();
    }

    public bool HasChanges => _overlay is not null;

    public ServiceSecretsFile? TakePendingUpdates()
    {
        var overlay = _overlay;
        _overlay = null;
        if (overlay is not null)
            Changed?.Invoke();
        return overlay;
    }

    public void ApplyTo(MediaServiceOptions options)
    {
        if (_overlay is null)
            return;

        ApplyIfSet(_overlay.SonarrApiKey, v => options.Sonarr.ApiKey = v);
        ApplyIfSet(_overlay.RadarrApiKey, v => options.Radarr.ApiKey = v);
        ApplyIfSet(_overlay.LidarrApiKey, v => options.Lidarr.ApiKey = v);
        ApplyIfSet(_overlay.ChaptarrApiKey, v => options.Chaptarr.ApiKey = v);
        ApplyIfSet(_overlay.AudiobookShelfApiKey, v => options.AudiobookShelf.ApiKey = v);
        ApplyIfSet(_overlay.SlskdApiKey, v => options.Slskd.ApiKey = v);
        ApplyIfSet(_overlay.PlexToken, v => options.Plex.Token = v);
        ApplyIfSet(_overlay.EmbyApiKey, v => options.Emby.ApiKey = v);
        ApplyIfSet(_overlay.JellyfinApiKey, v => options.Jellyfin.ApiKey = v);
        ApplyIfSet(_overlay.TmdbApiKey, v => options.Tmdb.ApiKey = v);
        ApplyIfSet(_overlay.SonarrUrl, v => options.Sonarr.Url = v);
        ApplyIfSet(_overlay.RadarrUrl, v => options.Radarr.Url = v);
        ApplyIfSet(_overlay.LidarrUrl, v => options.Lidarr.Url = v);
        ApplyIfSet(_overlay.ChaptarrUrl, v => options.Chaptarr.Url = v);
        ApplyIfSet(_overlay.AudiobookShelfUrl, v => options.AudiobookShelf.Url = v);
        ApplyIfSet(_overlay.SlskdUrl, v => options.Slskd.Url = v);
        ApplyIfSet(_overlay.PlexUrl, v => options.Plex.Url = v);
        ApplyIfSet(_overlay.EmbyUrl, v => options.Emby.Url = v);
        ApplyIfSet(_overlay.JellyfinUrl, v => options.Jellyfin.Url = v);
    }

    private static void ApplyIfSet(string? value, Action<string> apply)
    {
        if (!string.IsNullOrWhiteSpace(value))
            apply(value.Trim());
    }
}
