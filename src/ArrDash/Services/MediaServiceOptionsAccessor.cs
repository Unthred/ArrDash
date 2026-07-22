using ArrDash.Configuration;
using Microsoft.Extensions.Options;

namespace ArrDash.Services;

public sealed class MediaServiceOptionsAccessor
{
    private readonly IOptions<MediaServiceOptions> _options;
    private readonly ServiceSecretsStore _secrets;
    private readonly ServiceCredentialsPreviewService _preview;
    private MediaServiceOptions _current;

    public MediaServiceOptionsAccessor(
        IOptions<MediaServiceOptions> options,
        ServiceSecretsStore secrets,
        ServiceCredentialsPreviewService preview)
    {
        _options = options;
        _secrets = secrets;
        _preview = preview;
        _current = Build();
        _secrets.Changed += Reload;
        _preview.Changed += Reload;
    }

    public MediaServiceOptions Options => _current;

    public event Action? Changed;

    public void Reload()
    {
        _current = Build();
        Changed?.Invoke();
    }

    private MediaServiceOptions Build()
    {
        var clone = new MediaServiceOptions
        {
            Sonarr = CloneEndpoint(_options.Value.Sonarr),
            Radarr = CloneEndpoint(_options.Value.Radarr),
            Lidarr = CloneEndpoint(_options.Value.Lidarr),
            Chaptarr = CloneEndpoint(_options.Value.Chaptarr),
            AudiobookShelf = CloneEndpoint(_options.Value.AudiobookShelf),
            Slskd = CloneEndpoint(_options.Value.Slskd),
            Emby = CloneEndpoint(_options.Value.Emby),
            Jellyfin = CloneEndpoint(_options.Value.Jellyfin),
            Tautulli = CloneEndpoint(_options.Value.Tautulli),
            Tracearr = CloneEndpoint(_options.Value.Tracearr),
            Trakt = new TraktOptions
            {
                ClientId = _options.Value.Trakt.ClientId,
                ClientSecret = _options.Value.Trakt.ClientSecret
            },
            Tmdb = new TmdbOptions
            {
                ApiKey = _options.Value.Tmdb.ApiKey
            },
            Plex = new PlexOptions
            {
                Url = _options.Value.Plex.Url,
                Token = _options.Value.Plex.Token
            },
            PollIntervalSeconds = _options.Value.PollIntervalSeconds,
            RecentLimit = _options.Value.RecentLimit
        };

        _secrets.ApplyTo(clone);
        _preview.ApplyTo(clone);
        return clone;
    }

    private static ServiceEndpoint CloneEndpoint(ServiceEndpoint e) =>
        new() { Url = e.Url, ApiKey = e.ApiKey };
}
