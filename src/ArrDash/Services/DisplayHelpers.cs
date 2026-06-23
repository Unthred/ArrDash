using ArrDash.Models;

namespace ArrDash.Services;

public static class ByteDisplayHelper
{
    public static string Format(long bytes)
    {
        if (bytes < 0)
            return "0 B";

        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit >= 3 ? $"{size:0.#} {units[unit]}" : $"{size:0} {units[unit]}";
    }

    public static string FormatUsedTotal(long usedBytes, long totalBytes)
    {
        if (totalBytes <= 0)
            return Format(Math.Max(0, usedBytes));

        double total = totalBytes;
        var unit = 0;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        while (total >= 1024 && unit < units.Length - 1)
        {
            total /= 1024;
            unit++;
        }

        var divisor = Math.Pow(1024, unit);
        var used = usedBytes / divisor;
        var totalSize = totalBytes / divisor;
        var format = unit >= 3 ? "0.#" : "0.#";

        return $"{used.ToString(format, System.Globalization.CultureInfo.InvariantCulture)}/{totalSize.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}";
    }
}

public static class CountDisplayHelper
{
    public static string Format(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000d:0.#}M",
        >= 10_000 => $"{count / 1_000d:0.#}k",
        >= 1_000 => count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
        _ => count.ToString(System.Globalization.CultureInfo.CurrentCulture)
    };
}

public static class TimeDisplayHelper
{
    public static string Format(DateTimeOffset ts, TimeDisplayFormat format) => format switch
    {
        TimeDisplayFormat.Clock => ts.ToLocalTime().ToString("HH:mm"),
        TimeDisplayFormat.DateTime => ts.ToLocalTime().ToString("dd MMM HH:mm"),
        _ => FormatRelative(ts)
    };

    /// <summary>Last-refresh label: finer granularity than item timestamps so auto-poll does not read as stuck on "just now".</summary>
    public static string FormatLastRefresh(DateTimeOffset ts, TimeDisplayFormat format) => format switch
    {
        TimeDisplayFormat.Clock => ts.ToLocalTime().ToString("HH:mm:ss"),
        TimeDisplayFormat.DateTime => ts.ToLocalTime().ToString("dd MMM HH:mm:ss"),
        _ => FormatRefreshRelative(ts)
    };

    public static string FormatRelative(DateTimeOffset ts)
    {
        var delta = DateTimeOffset.UtcNow - ts.ToUniversalTime();
        if (delta.TotalMinutes < 1) return "just now";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalDays < 1) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return ts.ToLocalTime().ToString("dd MMM");
    }

    public static string FormatRefreshRelative(DateTimeOffset ts)
    {
        var delta = DateTimeOffset.UtcNow - ts.ToUniversalTime();
        if (delta.TotalSeconds < 5) return "just now";
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalDays < 1) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return ts.ToLocalTime().ToString("dd MMM");
    }
}

public static class RecentItemFilter
{
    public static int ResolveLimit(UserLayoutPreferences prefs, string panelId)
    {
        if (prefs.RecentWindowMode == RecentWindowMode.Days)
            return Math.Clamp(prefs.DefaultRecentLimit, 5, 100);

        if (prefs.RecentLimits.TryGetValue(panelId, out var panelLimit) && panelLimit > 0)
            return Math.Clamp(panelLimit, 5, 100);

        return Math.Clamp(prefs.DefaultRecentLimit, 5, 100);
    }

    public static IReadOnlyList<DownloadItem> Apply(
        IReadOnlyList<DownloadItem> items,
        UserLayoutPreferences prefs,
        string panelId)
    {
        IEnumerable<DownloadItem> query = items;

        if (prefs.RecentWindowMode == RecentWindowMode.Days)
        {
            var days = Math.Clamp(prefs.RecentDays, 1, 365);
            var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
            query = query.Where(i => i.Timestamp >= cutoff);
        }

        var limit = ResolveLimit(prefs, panelId);
        return query
            .OrderByDescending(i => i.Timestamp)
            .Take(limit)
            .ToList();
    }
}
