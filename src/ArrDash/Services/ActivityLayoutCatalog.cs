using ArrDash.Models;

namespace ArrDash.Services;

public static class ActivityLayoutCatalog
{
    public static IReadOnlyList<ActivityLayoutItem> CreateDefault() =>
    [
        new("plays-over-time", 1),
        new("peak-concurrent", 1),
        new("media-mix", 1),
        new("platforms", 1),
        new("stream-quality", 1),
        new("by-day", 1),
        new("by-hour", 1),
        new("top-users", 1),
        new("top-movies", 1),
        new("top-tv", 1),
        new("top-music", 1),
        new("top-genres", 1),
        new("popular-tv", 1),
        new("popular-movies", 1),
        new("top-libraries", 1),
        new("recently-watched", 2)
    ];

    public static IReadOnlyList<ActivityLayoutItem> Normalize(IReadOnlyList<ActivityLayoutItem>? saved)
    {
        var defaults = CreateDefault();
        if (saved is null || saved.Count == 0)
            return defaults;

        var known = defaults.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = saved
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && known.Contains(item.Id))
            .Select(item => new ActivityLayoutItem(item.Id, item.Span, item.Column))
            .ToList();

        foreach (var fallback in defaults)
        {
            if (merged.All(item => !string.Equals(item.Id, fallback.Id, StringComparison.OrdinalIgnoreCase)))
                merged.Add(new ActivityLayoutItem(fallback.Id, fallback.Span, fallback.Column));
        }

        return merged;
    }

    public static bool IsVisible(
        string id,
        WatchStatsSourceFilter filter,
        IReadOnlyList<string> configuredSources,
        bool hasMusic,
        bool hasGenres)
    {
        var keys = WatchStatsSourceFilters.ToSourceKeys(filter);
        var includes = (string source) =>
            keys.Count == 0
                ? configuredSources.Contains(source)
                : keys.Contains(source);
        var includesAnyMediaServer = includes(WatchStatsSources.Plex)
            || includes(WatchStatsSources.Emby)
            || includes(WatchStatsSources.Jellyfin);

        return id.ToLowerInvariant() switch
        {
            "media-mix" => includes(WatchStatsSources.Plex) && configuredSources.Contains(WatchStatsSources.Plex),
            "platforms" => keys.Count == 0 ? configuredSources.Count > 0 : keys.Any(configuredSources.Contains),
            "stream-quality" or "by-day" or "by-hour" =>
                keys.Count == 0
                    ? configuredSources.Any(s => s is WatchStatsSources.Plex or WatchStatsSources.Emby
                        or WatchStatsSources.Jellyfin or WatchStatsSources.Trakt)
                    : keys.Any(configuredSources.Contains),
            "peak-concurrent" => includesAnyMediaServer && configuredSources.Any(s =>
                s is WatchStatsSources.Plex or WatchStatsSources.Emby or WatchStatsSources.Jellyfin),
            "top-music" => hasMusic,
            "top-genres" => hasGenres,
            "popular-tv" or "popular-movies" or "top-libraries" => true,
            _ => true
        };
    }
}
