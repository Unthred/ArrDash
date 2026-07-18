namespace ArrDash.Configuration;

public sealed class MediaServiceOptions
{
    public const string SectionName = "MediaServices";

    public ServiceEndpoint Sonarr { get; set; } = new();
    public ServiceEndpoint Radarr { get; set; } = new();
    public ServiceEndpoint Lidarr { get; set; } = new();
    public ServiceEndpoint Chaptarr { get; set; } = new();
    public ServiceEndpoint AudiobookShelf { get; set; } = new();
    public ServiceEndpoint Slskd { get; set; } = new();
    public PlexOptions Plex { get; set; } = new();
    public ServiceEndpoint Emby { get; set; } = new();
    public ServiceEndpoint Jellyfin { get; set; } = new();
    public ServiceEndpoint Tautulli { get; set; } = new();
    public ServiceEndpoint Tracearr { get; set; } = new();
    public TraktOptions Trakt { get; set; } = new();
    public TmdbOptions Tmdb { get; set; } = new();
    public int PollIntervalSeconds { get; set; } = 30;
    public int RecentLimit { get; set; } = 20;
}

public sealed class ServiceEndpoint
{
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class PlexOptions
{
    public string Url { get; set; } = "";
    public string Token { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(Token);
}

public sealed class TmdbOptions
{
    public string ApiKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class TraktOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
