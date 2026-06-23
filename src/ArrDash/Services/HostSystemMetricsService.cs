using ArrDash.Models;

namespace ArrDash.Services;

public sealed class HostSystemMetricsService
{
    private readonly string _procStatPath;
    private readonly string _procMeminfoPath;
    private readonly string[] _diskPaths;
    private readonly string _label;
    private readonly object _cpuLock = new();
    private ulong _prevTotal;
    private ulong _prevIdle;
    private bool _hasCpuSample;

    public HostSystemMetricsService()
    {
        var procRoot = Environment.GetEnvironmentVariable("ARRDASH_PROC_ROOT") ?? "/proc";
        _procStatPath = Path.Combine(procRoot, "stat");
        _procMeminfoPath = Path.Combine(procRoot, "meminfo");
        _diskPaths = ResolveDiskPaths();
        _label = Environment.GetEnvironmentVariable("ARRDASH_HOST_LABEL") ?? "Server";
    }

    public ServerMetrics? Read()
    {
        try
        {
            var memory = ReadMemory();
            var disk = ReadDisk();
            if (memory is null || disk is null)
                return null;

            var cpu = ReadCpuPercent();
            return new ServerMetrics(
                _label,
                cpu,
                memory.Value.UsedPercent,
                memory.Value.UsedBytes,
                memory.Value.TotalBytes,
                disk.Value.UsedPercent,
                disk.Value.UsedBytes,
                disk.Value.TotalBytes);
        }
        catch
        {
            return null;
        }
    }

    private static string[] ResolveDiskPaths()
    {
        var configured = Environment.GetEnvironmentVariable("ARRDASH_DISK_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (Directory.Exists("/mnt/user"))
            return ["/mnt/user"];

        return ["/config"];
    }

    private double? ReadCpuPercent()
    {
        if (!File.Exists(_procStatPath))
            return null;

        var line = File.ReadLines(_procStatPath).FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
            return null;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5)
            return null;

        ulong total = 0;
        for (var i = 1; i < parts.Length; i++)
        {
            if (ulong.TryParse(parts[i], out var value))
                total += value;
        }

        if (!ulong.TryParse(parts[4], out var idle))
            return null;

        lock (_cpuLock)
        {
            if (!_hasCpuSample)
            {
                _prevTotal = total;
                _prevIdle = idle;
                _hasCpuSample = true;
                return null;
            }

            var totalDelta = total - _prevTotal;
            var idleDelta = idle - _prevIdle;
            _prevTotal = total;
            _prevIdle = idle;

            if (totalDelta == 0)
                return null;

            var usedPercent = (totalDelta - idleDelta) * 100.0 / totalDelta;
            return Math.Clamp(usedPercent, 0, 100);
        }
    }

    private (long TotalBytes, long UsedBytes, double UsedPercent)? ReadMemory()
    {
        if (!File.Exists(_procMeminfoPath))
            return null;

        long? totalKb = null;
        long? availableKb = null;

        foreach (var line in File.ReadLines(_procMeminfoPath))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                totalKb = ParseMeminfoKb(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                availableKb = ParseMeminfoKb(line);

            if (totalKb is not null && availableKb is not null)
                break;
        }

        if (totalKb is null or <= 0 || availableKb is null)
            return null;

        var totalBytes = totalKb.Value * 1024L;
        var usedBytes = Math.Max(0, totalBytes - (availableKb.Value * 1024L));
        var usedPercent = usedBytes * 100.0 / totalBytes;
        return (totalBytes, usedBytes, Math.Clamp(usedPercent, 0, 100));
    }

    private (long TotalBytes, long UsedBytes, double UsedPercent)? ReadDisk()
    {
        long totalBytes = 0;
        long freeBytes = 0;

        foreach (var path in _diskPaths)
        {
            if (!Directory.Exists(path))
                continue;

            try
            {
                var drive = new DriveInfo(path);
                if (!drive.IsReady || drive.TotalSize <= 0)
                    continue;

                totalBytes += drive.TotalSize;
                freeBytes += drive.AvailableFreeSpace;
            }
            catch
            {
                // Skip unreadable mounts.
            }
        }

        if (totalBytes <= 0)
            return null;

        var usedBytes = Math.Max(0, totalBytes - freeBytes);
        var usedPercent = usedBytes * 100.0 / totalBytes;
        return (totalBytes, usedBytes, Math.Clamp(usedPercent, 0, 100));
    }

    private static long? ParseMeminfoKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var kb) ? kb : null;
    }
}
