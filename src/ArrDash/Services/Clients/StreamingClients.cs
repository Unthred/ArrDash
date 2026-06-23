using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using ArrDash.Configuration;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Services.Clients;

public sealed class PlexClient(HttpClient http, MediaServiceOptionsAccessor options)
{
    private PlexOptions Plex => options.Options.Plex;

    public bool IsConfigured => Plex.IsConfigured;

    public async Task<(IReadOnlyList<ActiveSession> Sessions, ServiceHealth Health)> FetchSessionsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("plex", "Plex", false, false, null, null));

        try
        {
            var url = $"{Plex.Url.TrimEnd('/')}/status/sessions?X-Plex-Token={Uri.EscapeDataString(Plex.Token)}";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            var sessions = doc.Descendants("Video")
                .Concat(doc.Descendants("Track"))
                .Select(MapSession)
                .Where(s => s is not null)
                .Cast<ActiveSession>()
                .ToList();

            return (sessions, new ServiceHealth("plex", "Plex", true, true, null, null));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("plex", "Plex", true, false, ex.Message, null));
        }
    }

    private ActiveSession? MapSession(XElement el)
    {
        var sessionKey = el.Attribute("sessionKey")?.Value ?? el.Attribute("ratingKey")?.Value ?? Guid.NewGuid().ToString("N");
        var title = el.Attribute("title")?.Value ?? el.Attribute("grandparentTitle")?.Value ?? "Unknown";
        var subtitle = BuildSubtitle(el);
        var user = el.Attribute("user")?.Value
            ?? el.Descendants("User").FirstOrDefault()?.Attribute("title")?.Value
            ?? "Unknown";

        var duration = ParseLong(el.Attribute("duration")?.Value);
        var viewOffset = ParseLong(el.Attribute("viewOffset")?.Value);
        var progress = duration > 0 ? Math.Clamp(viewOffset * 100.0 / duration, 0, 100) : 0;

        var ratingKey = el.Attribute("ratingKey")?.Value;
        var thumb = el.Attribute("thumb")?.Value ?? el.Attribute("parentThumb")?.Value ?? el.Attribute("grandparentThumb")?.Value;
        var thumbUrl = thumb is not null ? PosterUrls.PlexThumb(thumb) : null;
        var externalUrl = ratingKey is not null
            ? $"{Plex.Url.TrimEnd('/')}/web/index.html#!/server/details?key=%2Flibrary%2Fmetadata%2F{Uri.EscapeDataString(ratingKey)}"
            : Plex.Url.TrimEnd('/');

        var player = el.Descendants("Player").FirstOrDefault();
        var device = player?.Attribute("title")?.Value ?? player?.Attribute("device")?.Value;

        var type = el.Attribute("type")?.Value ?? "video";

        return new ActiveSession(
            $"plex-{sessionKey}",
            StreamingServer.Plex,
            user,
            title,
            subtitle,
            type,
            Math.Round(progress, 1),
            device,
            thumbUrl,
            null,
            externalUrl);
    }

    private static string? BuildSubtitle(XElement el)
    {
        var grandparent = el.Attribute("grandparentTitle")?.Value;
        var parent = el.Attribute("parentTitle")?.Value;
        var index = el.Attribute("index")?.Value;
        var parentIndex = el.Attribute("parentIndex")?.Value;

        if (grandparent is not null && parentIndex is not null && index is not null)
            return $"{grandparent} · S{parentIndex}E{index}";

        if (parent is not null)
            return parent;

        return null;
    }

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}

public sealed class EmbyClient(HttpClient http, MediaServiceOptionsAccessor options)
{
    private ServiceEndpoint Emby => options.Options.Emby;

    public bool IsConfigured => Emby.IsConfigured;

    public async Task<(IReadOnlyList<ActiveSession> Sessions, ServiceHealth Health)> FetchSessionsAsync(CancellationToken ct)
    {
        if (!IsConfigured)
            return ([], new ServiceHealth("emby", "Emby", false, false, null, null));

        try
        {
            var url = $"{Emby.Url.TrimEnd('/')}/Sessions?api_key={Uri.EscapeDataString(Emby.ApiKey)}";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var sessions = new List<ActiveSession>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var session in doc.RootElement.EnumerateArray())
                {
                    var mapped = MapSession(session);
                    if (mapped is not null)
                        sessions.Add(mapped);
                }
            }

            return (sessions, new ServiceHealth("emby", "Emby", true, true, null, null));
        }
        catch (Exception ex)
        {
            return ([], new ServiceHealth("emby", "Emby", true, false, ex.Message, null));
        }
    }

    private ActiveSession? MapSession(JsonElement session)
    {
        if (!session.TryGetProperty("NowPlayingItem", out var item))
            return null;

        var id = session.TryGetProperty("Id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
        var user = session.TryGetProperty("UserName", out var u) ? u.GetString() ?? "Unknown" : "Unknown";
        var title = item.TryGetProperty("Name", out var n) ? n.GetString() ?? "Unknown" : "Unknown";
        var series = item.TryGetProperty("SeriesName", out var sn) ? sn.GetString() : null;
        var type = item.TryGetProperty("Type", out var t) ? t.GetString() ?? "Video" : "Video";

        double progress = 0;
        if (session.TryGetProperty("PlayState", out var ps))
        {
            var pos = ps.TryGetProperty("PositionTicks", out var pt) ? pt.GetInt64() : 0;
            var run = item.TryGetProperty("RunTimeTicks", out var rt) ? rt.GetInt64() : 0;
            if (run > 0)
                progress = Math.Clamp(pos * 100.0 / run, 0, 100);
        }

        string? thumbUrl = null;
        string? externalUrl = null;
        if (item.TryGetProperty("Id", out var itemId) && itemId.GetString() is { Length: > 0 } embyItemId)
        {
            thumbUrl = PosterUrls.EmbyItem(embyItemId);
            externalUrl = $"{Emby.Url.TrimEnd('/')}/web/index.html#!/item?id={Uri.EscapeDataString(embyItemId)}";
        }

        var device = session.TryGetProperty("Client", out var c) ? c.GetString() : null;

        return new ActiveSession(
            $"emby-{id}",
            StreamingServer.Emby,
            user,
            title,
            series,
            type,
            Math.Round(progress, 1),
            device,
            thumbUrl,
            null,
            externalUrl);
    }
}
