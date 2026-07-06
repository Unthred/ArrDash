using ArrDash.Models;
using ArrDash.Services.Clients;

namespace ArrDash.Services;

public sealed class LibraryStatsService(
    SonarrClient sonarr,
    RadarrClient radarr,
    LidarrClient lidarr,
    ChaptarrClient chaptarr,
    AudiobookShelfClient audiobookShelf,
    LayoutPreferencesService prefs)
{
    private readonly object _lock = new();
    private IReadOnlyList<LibraryStatItem> _cached = [];
    private DateTimeOffset _cachedAt;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<LibraryStatItem>> GetAsync(CancellationToken ct = default, bool force = false)
    {
        if (!force)
        {
            lock (_lock)
            {
                if (_cached.Count > 0 && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
                    return _cached;
            }
        }

        var tasks = new List<Task<LibraryStatItem?>>();
        if (prefs.IsServiceEnabled("sonarr"))
            tasks.Add(sonarr.FetchLibraryStatsAsync(ct));
        if (prefs.IsServiceEnabled("radarr"))
            tasks.Add(radarr.FetchLibraryStatsAsync(ct));
        if (prefs.IsServiceEnabled("chaptarr"))
            tasks.Add(chaptarr.FetchLibraryStatsAsync(ct));
        if (prefs.IsServiceEnabled("audiobookshelf"))
            tasks.Add(audiobookShelf.FetchLibraryStatsAsync(ct));
        if (prefs.IsServiceEnabled("lidarr"))
            tasks.Add(lidarr.FetchLibraryStatsAsync(ct));

        if (tasks.Count == 0)
            return [];

        var results = await Task.WhenAll(tasks);
        var items = results
            .Where(i => i is not null)
            .Cast<LibraryStatItem>()
            .ToList();

        items = MergeAudiobookStats(items);
        items = items.OrderBy(OrderKey).ToList();

        lock (_lock)
        {
            _cached = items;
            _cachedAt = DateTimeOffset.UtcNow;
        }

        return items;
    }

    private List<LibraryStatItem> MergeAudiobookStats(IReadOnlyList<LibraryStatItem> items)
    {
        var chaptarr = items.FirstOrDefault(i => i.Key == "chaptarr");
        var abs = items.FirstOrDefault(i => i.Key == "audiobookshelf");
        if (chaptarr is null && abs is null)
            return items.ToList();

        var others = items
            .Where(i => i.Key is not "chaptarr" and not "audiobookshelf")
            .ToList();

        switch (prefs.Current.AudiobookSource)
        {
            case AudiobookSourceMode.ChaptarrOnly:
                if (chaptarr is not null)
                    others.Add(chaptarr);
                break;
            case AudiobookSourceMode.AudiobookShelfOnly:
                if (abs is not null)
                    others.Add(abs);
                break;
            default:
                if (chaptarr is not null)
                    others.Add(WithCountDelta(chaptarr, abs, "AudioBookShelf"));
                if (abs is not null)
                    others.Add(WithCountDelta(abs, chaptarr, "Chaptarr"));
                break;
        }

        return others;
    }

    private static LibraryStatItem WithCountDelta(
        LibraryStatItem stat,
        LibraryStatItem? compare,
        string compareLabel)
    {
        if (compare?.ItemCount is not long otherCount || stat.ItemCount is not long count)
            return stat;

        return stat with { Detail = AppendCountDelta(stat.Detail, count, otherCount, compareLabel) };
    }

    private static string? AppendCountDelta(string? detail, long count, long otherCount, string otherLabel)
    {
        string suffix;
        if (count == otherCount)
            suffix = $"matches {otherLabel}";
        else
        {
            var diff = Math.Abs(count - otherCount);
            var diffText = diff == 1 ? "1 book" : $"{diff:N0} books";
            var relation = count > otherCount ? "more than" : "fewer than";
            suffix = $"{diffText} {relation} {otherLabel}";
        }

        return string.IsNullOrWhiteSpace(detail) ? suffix : $"{detail} · {suffix}";
    }

    private static int OrderKey(LibraryStatItem item) => item.Key switch
    {
        "sonarr" => 0,
        "radarr" => 1,
        "chaptarr" => 2,
        "audiobookshelf" => 3,
        "lidarr" => 4,
        _ => 99
    };
}
