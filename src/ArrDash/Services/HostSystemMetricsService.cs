using ArrDash.Models;

namespace ArrDash.Services;

public sealed class HostSystemMetricsService
{
    private readonly LayoutPreferencesService? _prefs;
    private readonly string _procStatPath;
    private readonly string _procMeminfoPath;
    private readonly object _cpuLock = new();
    private ulong _prevTotal;
    private ulong _prevIdle;
    private ulong _prevIoWait;
    private bool _hasCpuSample;
    private readonly object _perCoreLock = new();
    private readonly Dictionary<int, (ulong Total, ulong Idle)> _prevPerCore = new();
    private readonly string _sysCpuPath;
    private Dictionary<int, int>? _coreIdByLogicalIndex;

    public HostSystemMetricsService(LayoutPreferencesService prefs)
    {
        _prefs = prefs;
        var procRoot = Environment.GetEnvironmentVariable("ARRDASH_PROC_ROOT") ?? "/proc";
        _procStatPath = Path.Combine(procRoot, "stat");
        _procMeminfoPath = Path.Combine(procRoot, "meminfo");
        _sysCpuPath = Environment.GetEnvironmentVariable("ARRDASH_SYS_CPU_ROOT") ?? "/sys/devices/system/cpu";
    }

    public ServerMetrics? Read()
    {
        try
        {
            var memory = ReadMemory();
            var disk = ReadDisk(ResolveDiskPaths(_prefs?.Current));
            if (memory is null || disk is null)
                return null;

            var (cpu, ioWait) = ReadCpuPercent();
            return new ServerMetrics(
                ResolveLabel(_prefs?.Current),
                cpu,
                ioWait,
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

    internal static string ResolveLabel(UserLayoutPreferences? p)
    {
        var fromPrefs = p?.MetricsHostLabel?.Trim();
        if (!string.IsNullOrWhiteSpace(fromPrefs))
            return fromPrefs;

        var fromEnv = Environment.GetEnvironmentVariable("ARRDASH_HOST_LABEL")?.Trim();
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return "Host";
    }

    internal static string[] ResolveDiskPaths(UserLayoutPreferences? p)
    {
        var fromPrefs = p?.MetricsDiskPath?.Trim();
        var fromEnv = Environment.GetEnvironmentVariable("ARRDASH_DISK_PATH")?.Trim();
        var configured = !string.IsNullOrWhiteSpace(fromPrefs) ? fromPrefs : fromEnv;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (Directory.Exists("/"))
            return ["/"];

        return ["/config"];
    }

    /// <summary>
    /// Returns genuine CPU-busy % and "waiting on disk" % (iowait) as separate figures. iowait is
    /// summed into /proc/stat's total jiffies but was previously left inside "busy" time — meaning
    /// a host stuck waiting on storage looked identical to one doing genuine CPU work. Both values
    /// come from the same delta computation/lock since they share the same previous-sample baseline.
    /// </summary>
    public (double? CpuPercent, double? IoWaitPercent) ReadCpuPercent()
    {
        if (!File.Exists(_procStatPath))
            return (null, null);

        var line = File.ReadLines(_procStatPath).FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
            return (null, null);

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6)
            return (null, null);

        ulong total = 0;
        for (var i = 1; i < parts.Length; i++)
        {
            if (ulong.TryParse(parts[i], out var value))
                total += value;
        }

        if (!ulong.TryParse(parts[4], out var idle) || !ulong.TryParse(parts[5], out var ioWait))
            return (null, null);

        lock (_cpuLock)
        {
            if (!_hasCpuSample)
            {
                _prevTotal = total;
                _prevIdle = idle;
                _prevIoWait = ioWait;
                _hasCpuSample = true;
                return (null, null);
            }

            var totalDelta = total - _prevTotal;
            var idleDelta = idle - _prevIdle;
            var ioWaitDelta = ioWait - _prevIoWait;
            _prevTotal = total;
            _prevIdle = idle;
            _prevIoWait = ioWait;

            if (totalDelta == 0)
                return (null, null);

            var busyPercent = (totalDelta - idleDelta - ioWaitDelta) * 100.0 / totalDelta;
            var ioWaitPercent = ioWaitDelta * 100.0 / totalDelta;
            return (Math.Clamp(busyPercent, 0, 100), Math.Clamp(ioWaitPercent, 0, 100));
        }
    }

    /// <summary>
    /// Returns one entry per physical core (averaging hyperthread sibling logical CPUs together),
    /// keyed by the same core id coretemp's "Core N" hwmon labels use — read from
    /// /sys/devices/system/cpu/cpuN/topology/core_id so usage bars line up with real temp sensors.
    /// </summary>
    public IReadOnlyList<CoreUsage> ReadPerCoreUsage()
    {
        if (!File.Exists(_procStatPath))
            return [];

        var perLogical = new List<(int Index, double Percent)>();

        lock (_perCoreLock)
        {
            foreach (var line in File.ReadLines(_procStatPath))
            {
                if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.Length < 4 || !char.IsDigit(line[3]))
                    continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                    continue;

                var label = parts[0][3..];
                if (!int.TryParse(label, out var index))
                    continue;

                ulong total = 0;
                for (var i = 1; i < parts.Length; i++)
                {
                    if (ulong.TryParse(parts[i], out var value))
                        total += value;
                }

                if (!ulong.TryParse(parts[4], out var idle))
                    continue;

                if (_prevPerCore.TryGetValue(index, out var prev))
                {
                    var totalDelta = total - prev.Total;
                    var idleDelta = idle - prev.Idle;
                    if (totalDelta > 0)
                        perLogical.Add((index, Math.Clamp((totalDelta - idleDelta) * 100.0 / totalDelta, 0, 100)));
                }

                _prevPerCore[index] = (total, idle);
            }
        }

        var topology = LoadTopology();
        return perLogical
            .GroupBy(l => topology.TryGetValue(l.Index, out var coreId) ? coreId : l.Index)
            .Select(g => new CoreUsage(g.Key, g.Average(l => l.Percent)))
            .OrderBy(c => c.Index)
            .ToList();
    }

    private Dictionary<int, int> LoadTopology()
    {
        if (_coreIdByLogicalIndex is not null)
            return _coreIdByLogicalIndex;

        var map = new Dictionary<int, int>();
        try
        {
            foreach (var dir in Directory.GetDirectories(_sysCpuPath, "cpu*"))
            {
                var name = Path.GetFileName(dir);
                if (!int.TryParse(name.AsSpan(3), out var logicalIndex))
                    continue;

                var coreIdPath = Path.Combine(dir, "topology", "core_id");
                if (File.Exists(coreIdPath) && int.TryParse(File.ReadAllText(coreIdPath).Trim(), out var coreId))
                    map[logicalIndex] = coreId;
            }
        }
        catch
        {
            // fall through with whatever was resolved
        }

        _coreIdByLogicalIndex = map;
        return map;
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

    private static (long TotalBytes, long UsedBytes, double UsedPercent)? ReadDisk(string[] diskPaths)
    {
        long totalBytes = 0;
        long freeBytes = 0;

        foreach (var path in diskPaths)
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
