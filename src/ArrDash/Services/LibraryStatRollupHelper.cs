using ArrDash.Models;

namespace ArrDash.Services;

public static class LibraryStatRollupHelper
{
    public static string RollupLabel(LibraryStatItem stat) => stat.Key switch
    {
        "sonarr" => "TV Shows",
        "radarr" => "Movies",
        "chaptarr" => "Chaptarr",
        "audiobookshelf" => "ABS",
        "lidarr" => "Music",
        _ => stat.Label
    };

    public static string RollupCount(LibraryStatItem stat)
    {
        if (stat.ItemCount is long count)
            return CountDisplayHelper.Format(count);

        var token = stat.Headline.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(token) ? stat.Headline : token;
    }
}
