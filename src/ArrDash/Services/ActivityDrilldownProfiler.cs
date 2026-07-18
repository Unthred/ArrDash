using System.Text.Json;
using ArrDash.Data.Entities;
using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Builds rich drill-down profiles (who watched, top genres/titles, media mix) from play events.
/// </summary>
public static class ActivityDrilldownProfiler
{
    public const int BreakdownLimit = 8;
    public const int PlayLimit = 50;

    public sealed record Event(
        string UserDisplayName,
        string? UserExternalId,
        string Title,
        string? SeriesTitle,
        string MediaType,
        IReadOnlyList<string> Genres,
        DateTimeOffset PlayedAtUtc,
        int DurationSeconds,
        string? Source,
        string? ThumbUrl,
        string? Platform = null);

    public static Event FromEntity(PlayEventEntity e) => new(
        e.UserDisplayName,
        e.UserExternalId,
        e.Title,
        e.SeriesTitle,
        e.MediaType,
        ReadGenres(e.GenresJson),
        new DateTimeOffset(DateTime.SpecifyKind(e.PlayedAtUtc, DateTimeKind.Utc)),
        e.DurationSeconds,
        e.Source,
        BuildThumb(e),
        e.Platform ?? e.Client);

    public static Event FromPlay(ActivityDrilldownPlay play) => new(
        play.UserDisplayName,
        play.UserExternalId,
        play.Title,
        play.Subtitle,
        play.MediaType ?? "other",
        play.Genres ?? [],
        play.PlayedAtUtc,
        (int)Math.Round(play.Hours * 3600),
        play.Source,
        play.ThumbUrl,
        play.Platform);

    public static ActivityDrilldownSnapshot Build(
        ActivityDrilldownRequest request,
        IReadOnlyList<Event> events,
        int breakdownLimit = BreakdownLimit,
        int playLimit = PlayLimit)
    {
        var matched = Filter(request, events);
        var totalSeconds = matched.Sum(e => e.DurationSeconds);
        var totalHours = Math.Round(totalSeconds / 3600.0, 1);
        var plays = matched
            .OrderByDescending(e => e.PlayedAtUtc)
            .Take(playLimit)
            .Select(ToPlay)
            .ToList();

        var uniqueUsers = matched
            .Select(e => e.UserDisplayName)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var uniqueTitles = matched
            .Select(DisplayTitle)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return request.Kind switch
        {
            ActivityDrilldownKind.User => new ActivityDrilldownSnapshot(
                request,
                plays,
                totalHours,
                matched.Count,
                uniqueUsers,
                uniqueTitles,
                TopUsers: null,
                TopMovies: BuildTopMedia(matched, "movie", breakdownLimit),
                TopTv: BuildTopMedia(matched, "episode", breakdownLimit),
                TopGenres: BuildTopGenres(matched, breakdownLimit),
                MediaMix: BuildMediaMix(matched)),

            ActivityDrilldownKind.Media => new ActivityDrilldownSnapshot(
                request,
                plays,
                totalHours,
                matched.Count,
                uniqueUsers,
                uniqueTitles,
                TopUsers: BuildTopUsers(matched, breakdownLimit),
                TopMovies: null,
                TopTv: null,
                TopGenres: BuildTopGenres(matched, breakdownLimit),
                MediaMix: null),

            ActivityDrilldownKind.Genre
                or ActivityDrilldownKind.Platform
                or ActivityDrilldownKind.MediaType
                or ActivityDrilldownKind.Quality => new ActivityDrilldownSnapshot(
                request,
                plays,
                totalHours,
                matched.Count,
                uniqueUsers,
                uniqueTitles,
                TopUsers: BuildTopUsers(matched, breakdownLimit),
                TopMovies: BuildTopMedia(matched, "movie", breakdownLimit),
                TopTv: BuildTopMedia(matched, "episode", breakdownLimit),
                TopGenres: request.Kind == ActivityDrilldownKind.Genre ? null : BuildTopGenres(matched, breakdownLimit),
                MediaMix: BuildMediaMix(matched)),

            _ => new ActivityDrilldownSnapshot(request, plays, totalHours, matched.Count, uniqueUsers, uniqueTitles)
        };
    }

    public static IReadOnlyList<Event> Filter(ActivityDrilldownRequest request, IReadOnlyList<Event> events)
    {
        IEnumerable<Event> filtered = events;

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            filtered = filtered.Where(e =>
                string.Equals(e.Source, request.Source, StringComparison.OrdinalIgnoreCase));
        }

        filtered = request.Kind switch
        {
            ActivityDrilldownKind.User => filtered.Where(e => MatchesUser(e, request.Key, request.Title)),
            ActivityDrilldownKind.Media => filtered.Where(e => MatchesMedia(e, request.Key, request.Title)),
            ActivityDrilldownKind.Genre => filtered.Where(e => MatchesGenre(e, request.Key, request.Title)),
            ActivityDrilldownKind.Platform => filtered.Where(e => MatchesPlatform(e, request.Key, request.Title)),
            ActivityDrilldownKind.MediaType => filtered.Where(e => MatchesMediaType(e, request.Key, request.Title)),
            ActivityDrilldownKind.Library => filtered,
            ActivityDrilldownKind.Quality => filtered,
            _ => filtered
        };

        return filtered.ToList();
    }

    private static bool MatchesUser(Event e, string key, string title)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (string.Equals(e.UserDisplayName, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.UserExternalId, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return !string.IsNullOrWhiteSpace(title)
            && string.Equals(e.UserDisplayName, title, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesMedia(Event e, string key, string title)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && (key.StartsWith("r:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("g:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("tid:", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(title))
                return true;

            return TitleMatches(e, title);
        }

        var needle = !string.IsNullOrWhiteSpace(key) ? key : title;
        if (string.IsNullOrWhiteSpace(needle))
            return false;

        return TitleMatches(e, needle)
            || (!string.IsNullOrWhiteSpace(title) && TitleMatches(e, title));
    }

    private static bool TitleMatches(Event e, string title) =>
        string.Equals(e.Title, title, StringComparison.OrdinalIgnoreCase)
        || string.Equals(e.SeriesTitle, title, StringComparison.OrdinalIgnoreCase)
        || e.Title.Contains(title, StringComparison.OrdinalIgnoreCase)
        || (e.SeriesTitle?.Contains(title, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool MatchesGenre(Event e, string key, string title)
    {
        var needle = !string.IsNullOrWhiteSpace(key) ? key : title;
        if (string.IsNullOrWhiteSpace(needle))
            return false;

        return e.Genres.Any(g => string.Equals(g, needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPlatform(Event e, string key, string title)
    {
        var needle = !string.IsNullOrWhiteSpace(key) ? key : title;
        if (string.IsNullOrWhiteSpace(needle))
            return false;

        return string.Equals(e.Platform, needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesMediaType(Event e, string key, string title)
    {
        var raw = (!string.IsNullOrWhiteSpace(key) ? key : title)?.Trim().ToLowerInvariant() ?? "";
        var expected = raw switch
        {
            "movie" or "movies" => "movie",
            "episode" or "episodes" or "tv" or "television" => "episode",
            "music" or "track" or "tracks" or "audio" => "music",
            _ => raw
        };
        return string.Equals(e.MediaType, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<WatchStatRow> BuildTopUsers(IReadOnlyList<Event> events, int limit) =>
        events
            .Where(e => !string.IsNullOrWhiteSpace(e.UserDisplayName)
                && !string.Equals(e.UserDisplayName, "Unknown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.UserDisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new WatchStatRow(
                g.Key,
                $"{g.Count()} plays",
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                null,
                null,
                g.Select(e => e.Source).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                DrilldownKey: g.Select(e => e.UserExternalId).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? g.Key,
                DrilldownKind: ActivityDrilldownKind.User))
            .OrderByDescending(r => r.Hours)
            .ThenByDescending(r => r.Plays)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopMedia(IReadOnlyList<Event> events, string mediaType, int limit) =>
        events
            .Where(e => string.Equals(e.MediaType, mediaType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => DisplayTitle(e), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new WatchStatRow(
                g.Key,
                mediaType == "episode"
                    ? g.Select(e => e.SeriesTitle).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                    : null,
                Math.Round(g.Sum(e => e.DurationSeconds) / 3600.0, 1),
                g.Count(),
                g.Select(e => e.ThumbUrl).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                null,
                g.Select(e => e.Source).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                DrilldownKey: g.Key,
                DrilldownKind: ActivityDrilldownKind.Media))
            .OrderByDescending(r => r.Hours)
            .ThenByDescending(r => r.Plays)
            .Take(limit)
            .ToList();

    private static IReadOnlyList<WatchStatRow> BuildTopGenres(IReadOnlyList<Event> events, int limit)
    {
        var genreHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var genrePlays = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in events)
        {
            foreach (var genre in ev.Genres.Where(g => !string.IsNullOrWhiteSpace(g)))
            {
                genreHours[genre] = genreHours.GetValueOrDefault(genre) + ev.DurationSeconds / 3600.0;
                genrePlays[genre] = genrePlays.GetValueOrDefault(genre) + 1;
            }
        }

        return genreHours
            .Select(kv => new WatchStatRow(
                kv.Key,
                $"{genrePlays[kv.Key]} plays",
                Math.Round(kv.Value, 1),
                genrePlays[kv.Key],
                null,
                null,
                null,
                DrilldownKey: kv.Key,
                DrilldownKind: ActivityDrilldownKind.Genre))
            .OrderByDescending(r => r.Hours)
            .Take(limit)
            .ToList();
    }

    private static ActivityDrilldownMediaMix BuildMediaMix(IReadOnlyList<Event> events)
    {
        double movies = 0, tv = 0, music = 0, other = 0;
        foreach (var ev in events)
        {
            var hours = ev.DurationSeconds / 3600.0;
            switch (ev.MediaType.ToLowerInvariant())
            {
                case "movie":
                    movies += hours;
                    break;
                case "episode":
                    tv += hours;
                    break;
                case "music":
                    music += hours;
                    break;
                default:
                    other += hours;
                    break;
            }
        }

        return new ActivityDrilldownMediaMix(
            Math.Round(movies, 1),
            Math.Round(tv, 1),
            Math.Round(music, 1),
            Math.Round(other, 1));
    }

    private static string DisplayTitle(Event e) =>
        string.IsNullOrWhiteSpace(e.Title) ? e.SeriesTitle ?? "" : e.Title;

    private static ActivityDrilldownPlay ToPlay(Event e) => new(
        e.Title,
        string.Equals(e.MediaType, "episode", StringComparison.OrdinalIgnoreCase) ? e.SeriesTitle : null,
        e.UserDisplayName,
        Math.Round(e.DurationSeconds / 3600.0, 2),
        e.PlayedAtUtc,
        e.ThumbUrl,
        e.Source,
        e.MediaType,
        e.Genres,
        e.UserExternalId);

    private static IReadOnlyList<string> ReadGenres(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // Same ThumbPath-first precedence as PlayEventAnalyticsService.BuildThumb (#45):
    // Tracearr-sourced Emby/Jellyfin rows carry a working proxied URL there.
    private static string? BuildThumb(PlayEventEntity e) =>
        e.Source switch
        {
            WatchStatsSources.Plex when !string.IsNullOrWhiteSpace(e.ThumbPath) => PosterUrls.PlexThumb(e.ThumbPath),
            WatchStatsSources.Emby when !string.IsNullOrWhiteSpace(e.ThumbPath) => e.ThumbPath,
            WatchStatsSources.Emby when !string.IsNullOrWhiteSpace(e.ExternalItemId) => PosterUrls.EmbyItem(e.ExternalItemId),
            WatchStatsSources.Jellyfin when !string.IsNullOrWhiteSpace(e.ThumbPath) => e.ThumbPath,
            WatchStatsSources.Jellyfin when !string.IsNullOrWhiteSpace(e.ExternalItemId) => PosterUrls.JellyfinItem(e.ExternalItemId),
            WatchStatsSources.Trakt => PosterUrls.Media(
                e.MediaType,
                e.TmdbId,
                e.ImdbId,
                e.MediaType == "episode" ? e.SeriesTitle ?? e.Title : e.Title,
                e.Year),
            _ => null
        };
}
