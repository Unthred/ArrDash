using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

public static class ArrActivityDetector
{
    private static readonly (ServiceWorkloadKind Kind, int Priority, Func<string, bool> Match, string Label)[] CommandRules =
    [
        (ServiceWorkloadKind.Importing, 1, n => n.Contains("Downloaded", StringComparison.OrdinalIgnoreCase) && n.Contains("Scan", StringComparison.OrdinalIgnoreCase), "Library import scan"),
        (ServiceWorkloadKind.Importing, 1, n => n.Equals("ManualImport", StringComparison.OrdinalIgnoreCase), "Manual import"),
        (ServiceWorkloadKind.Importing, 1, n => n.Equals("ImportList", StringComparison.OrdinalIgnoreCase) || n.Equals("BulkImport", StringComparison.OrdinalIgnoreCase), "Import list"),
        (ServiceWorkloadKind.Searching, 3, n => n.Contains("Search", StringComparison.OrdinalIgnoreCase), "Search"),
        (ServiceWorkloadKind.Syncing, 5, n => n.Equals("RssSync", StringComparison.OrdinalIgnoreCase), "RSS sync"),
        (ServiceWorkloadKind.Syncing, 5, n => n.StartsWith("Refresh", StringComparison.OrdinalIgnoreCase), "Refresh library"),
        (ServiceWorkloadKind.Syncing, 5, n => n.Equals("RescanFolders", StringComparison.OrdinalIgnoreCase), "Rescan folders"),
        (ServiceWorkloadKind.Syncing, 5, n => n.Equals("ApplicationUpdate", StringComparison.OrdinalIgnoreCase), "Application update"),
    ];

    public static ServiceWorkload? Detect(
        JsonElement? commands,
        JsonElement? queueStatus,
        JsonElement? queue)
    {
        var workloads = new List<(int Priority, ServiceWorkload Workload)>();

        foreach (var command in EnumerateActiveCommands(commands)
                     .Where(c => string.Equals(c.Status, "started", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryClassifyCommand(command.Name, out var kind, out var label))
                continue;

            workloads.Add((PriorityFor(kind), new ServiceWorkload(kind, label)));
        }

        if (queue is { } queueRoot)
        {
            var (importCount, downloadCount) = ClassifyActiveQueueItems(queueRoot);
            if (importCount > 0)
            {
                workloads.Add((PriorityFor(ServiceWorkloadKind.Importing),
                    new ServiceWorkload(ServiceWorkloadKind.Importing, $"Importing ({importCount})", importCount)));
            }
            else if (downloadCount > 0)
            {
                workloads.Add((PriorityFor(ServiceWorkloadKind.Downloading),
                    new ServiceWorkload(ServiceWorkloadKind.Downloading, $"Downloading ({downloadCount})", downloadCount)));
            }
        }

        if (workloads.Count == 0)
            return null;

        var ordered = workloads.OrderBy(w => w.Priority).ToList();
        var primary = ordered[0].Workload;
        if (ordered.Count == 1)
            return primary;

        var combinedLabel = string.Join(" · ", ordered.Select(w => w.Workload.Label).Distinct());
        return primary with { Label = combinedLabel };
    }

    private static IEnumerable<(string Name, string Status)> EnumerateActiveCommands(JsonElement? commands)
    {
        if (commands is not { } root)
            yield break;

        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array
                ? records
                : default;

        if (array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var command in array.EnumerateArray())
        {
            if (!command.TryGetProperty("status", out var statusEl))
                continue;

            var status = statusEl.GetString();
            if (!string.Equals(status, "started", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = command.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            yield return (name, status!);
        }
    }

    private static bool TryClassifyCommand(string name, out ServiceWorkloadKind kind, out string label)
    {
        foreach (var rule in CommandRules)
        {
            if (!rule.Match(name))
                continue;

            kind = rule.Kind;
            label = rule.Label == "Search" ? HumanizeCommandName(name) : rule.Label;
            return true;
        }

        kind = ServiceWorkloadKind.None;
        label = string.Empty;
        return false;
    }

    private static (int ImportCount, int DownloadCount) ClassifyActiveQueueItems(JsonElement queue)
    {
        var importCount = 0;
        var downloadCount = 0;

        var array = queue.ValueKind == JsonValueKind.Array
            ? queue
            : queue.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array
                ? records
                : default;

        if (array.ValueKind != JsonValueKind.Array)
            return (0, 0);

        foreach (var item in array.EnumerateArray())
        {
            if (IsActivelyImportingQueueItem(item))
            {
                importCount++;
                continue;
            }

            if (IsDownloadingQueueItem(item))
                downloadCount++;
        }

        return (importCount, downloadCount);
    }

    private static bool IsActivelyImportingQueueItem(JsonElement item)
    {
        if (item.TryGetProperty("trackedDownloadState", out var tracked) &&
            string.Equals(tracked.GetString(), "importing", StringComparison.OrdinalIgnoreCase))
            return true;

        if (item.TryGetProperty("trackedDownloadStatus", out var trackedStatus) &&
            string.Equals(trackedStatus.GetString(), "importing", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsDownloadingQueueItem(JsonElement item)
    {
        if (item.TryGetProperty("trackedDownloadState", out var tracked) &&
            string.Equals(tracked.GetString(), "downloading", StringComparison.OrdinalIgnoreCase))
            return true;

        if (item.TryGetProperty("status", out var statusEl) &&
            string.Equals(statusEl.GetString(), "downloading", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static int PriorityFor(ServiceWorkloadKind kind) => kind switch
    {
        ServiceWorkloadKind.Importing => 1,
        ServiceWorkloadKind.Scanning or ServiceWorkloadKind.Matching => 2,
        ServiceWorkloadKind.Searching => 3,
        ServiceWorkloadKind.Downloading => 4,
        ServiceWorkloadKind.Syncing => 5,
        ServiceWorkloadKind.Transcoding => 6,
        _ => 99
    };

    private static string HumanizeCommandName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Search";

        var buffer = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                buffer.Append(' ');
            buffer.Append(i == 0 ? char.ToUpper(c) : char.ToLower(c));
        }

        return buffer.ToString();
    }
}
