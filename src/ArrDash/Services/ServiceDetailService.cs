using ArrDash.Models;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public sealed class ServiceDetailService(
    SonarrClient sonarr,
    RadarrClient radarr,
    LidarrClient lidarr,
    ChaptarrClient chaptarr,
    AudiobookShelfClient audiobookShelf,
    PlexClient plex,
    EmbyClient emby,
    JellyfinClient jellyfin,
    MediaServiceOptionsAccessor options,
    LayoutPreferencesService prefs)
{
    public async Task<ServiceDetail?> FetchAsync(string serviceKey, CancellationToken ct)
    {
        var key = serviceKey.Trim().ToLowerInvariant();
        if (!prefs.IsServiceEnabled(key) && key is not "slskd")
            return DisabledDetail(key);

        return key switch
        {
            "sonarr" => await sonarr.FetchServiceDetailAsync(ct),
            "radarr" => await radarr.FetchServiceDetailAsync(ct),
            "lidarr" => await lidarr.FetchServiceDetailAsync(ct),
            "chaptarr" => await chaptarr.FetchServiceDetailAsync(ct),
            "audiobookshelf" => await audiobookShelf.FetchServiceDetailAsync(ct),
            "plex" => await FetchStreamingDetailAsync("plex", plex.FetchSessionsAsync(ct), options.Options.Plex.Url),
            "emby" => await FetchStreamingDetailAsync("emby", emby.FetchSessionsAsync(ct), options.Options.Emby.Url),
            "jellyfin" => await FetchStreamingDetailAsync("jellyfin", jellyfin.FetchSessionsAsync(ct), options.Options.Jellyfin.Url),
            "slskd" => FetchSlskdDetail(),
            _ => UnrecognizedDetail(key)
        };
    }

    private static ServiceDetail UnrecognizedDetail(string key)
    {
        var label = string.IsNullOrWhiteSpace(key) ? "Service" : key;
        return new ServiceDetail(
            key,
            label,
            false,
            false,
            null,
            null,
            DateTimeOffset.UtcNow,
            ServiceAttentionLevel.NotConfigured,
            "Unknown",
            null,
            [new ServiceProblem("warning", $"No detail view is available for \"{label}\".")],
            [],
            0,
            false,
            false,
            [],
            [],
            [],
            null);
    }

    private static string DisplayNameForKey(string key) =>
        key switch
        {
            "audiobookshelf" => "AudioBookShelf",
            "slskd" => "slskd",
            _ when string.IsNullOrWhiteSpace(key) => "Service",
            _ => char.ToUpperInvariant(key[0]) + key[1..]
        };

    private static ServiceDetail DisabledDetail(string key) =>
        new(
            key,
            DisplayNameForKey(key),
            false,
            false,
            null,
            null,
            DateTimeOffset.UtcNow,
            ServiceAttentionLevel.NotConfigured,
            "Disabled",
            null,
            [new ServiceProblem("info", "Disabled in Settings")],
            [],
            0,
            false,
            false,
            [],
            [],
            [],
            null);

    private static async Task<ServiceDetail> FetchStreamingDetailAsync(
        string key,
        Task<(IReadOnlyList<ActiveSession> Sessions, ServiceHealth Health)> fetchTask,
        string? serviceUrl)
    {
        var name = char.ToUpper(key[0]) + key[1..];
        try
        {
            var (sessions, health) = await fetchTask;
            return ArrServiceDetailParser.BuildStreamingDetail(
                key,
                name,
                serviceUrl,
                health.Configured,
                health.Online,
                health.Error,
                sessions,
                health.Workload);
        }
        catch (Exception ex)
        {
            return ArrServiceDetailParser.BuildStreamingDetail(
                key, name, serviceUrl, true, false, ex.Message, [], null);
        }
    }

    private ServiceDetail FetchSlskdDetail()
    {
        const string key = "slskd";
        var endpoint = options.Options.Slskd;
        if (!endpoint.IsConfigured)
            return ArrServiceDetailParser.NotConfigured(key, "slskd");

        return ArrServiceDetailParser.BuildSimpleDetail(
            key,
            "slskd",
            endpoint.Url,
            true,
            false,
            null,
            "Connected status not yet monitored — API key is configured",
            null);
    }
}
