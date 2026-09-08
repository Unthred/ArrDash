using ArrDash.Models;

namespace ArrDash.Services;

public static class DownloadSummaryBuilder
{
    public static DownloadSummary Build(
        int windowHours,
        IReadOnlyList<DownloadItem> tv,
        IReadOnlyList<DownloadItem> movies,
        IReadOnlyList<DownloadItem> audiobooks,
        IReadOnlyList<DownloadItem> music,
        DateTimeOffset? now = null)
    {
        var hours = Math.Clamp(windowHours, 1, 24 * 31);
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddHours(-hours);
        return new DownloadSummary(hours,
        [
            Count(MediaSource.Sonarr, "Sonarr", tv, cutoff),
            Count(MediaSource.Radarr, "Radarr", movies, cutoff),
            Count(MediaSource.Chaptarr, "Chaptarr", audiobooks, cutoff),
            Count(MediaSource.Lidarr, "Lidarr", music, cutoff)
        ]);
    }

    private static DownloadSummaryItem Count(MediaSource source, string label, IEnumerable<DownloadItem> items, DateTimeOffset cutoff) =>
        new(source, label,
            items.Count(item => item.Timestamp >= cutoff && item.Event is DownloadEvent.Imported or DownloadEvent.Completed),
            items.Count(item => item.Timestamp >= cutoff && item.Event == DownloadEvent.Grabbed));
}
