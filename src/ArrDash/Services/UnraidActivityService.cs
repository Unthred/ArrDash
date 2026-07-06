using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed class UnraidActivityService : IDisposable
{
    private static readonly TimeSpan RecentlyUpdatedWindow = TimeSpan.FromSeconds(60);
    // Docker's non-streaming stats endpoint is slow per-call; with dozens of containers even
    // bounded concurrency adds up, so this refreshes less often than the cheap file-read signals.
    // Kept short enough that the "Top CPU"/"What's using memory" tiles stay roughly in sync with
    // the live-updating aggregate gauges — a stale reading next to a live one was the whole bug.
    // ToggleDetail() also force-bypasses this cache the moment the user opens the detail panel.
    private static readonly TimeSpan TopContainerCacheTtl =
        TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable("ARRDASH_TOP_CONTAINER_CACHE_SECONDS"), out var cacheSeconds)
            ? Math.Clamp(cacheSeconds, 5, 300)
            : 20);
    private const int MaxConcurrentStatsCalls = 8;

    private readonly string _varIniPath;
    private readonly string _disksIniPath;
    private readonly bool _isUnraidHost;
    private readonly HttpClient? _dockerHttp;
    private readonly object _containerLock = new();
    private Dictionary<string, string> _lastRunningIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _recentlyUpdated = new(StringComparer.Ordinal);

    private readonly object _moverLock = new();
    private DateTimeOffset? _moverStartedAt;

    private readonly object _topContainerLock = new();
    private IReadOnlyList<ContainerUsage> _topContainersCached = [];
    private DateTimeOffset _topContainerCachedAt;

    private readonly string _hostProcRootPath;
    private readonly string _diskStatsPath;
    private readonly HostNetworkSamplerService _hostNetworkSampler;

    private readonly object _containerNetworkLock = new();
    private Dictionary<string, (long Rx, long Tx, DateTimeOffset At)> _prevContainerNetwork = new(StringComparer.Ordinal);

    private readonly object _diskIoLock = new();
    private readonly Dictionary<string, (ulong TimeDoingIoMs, DateTimeOffset At)> _prevDiskIo = new(StringComparer.Ordinal);

    public UnraidActivityService(HostNetworkSamplerService hostNetworkSampler)
    {
        _hostNetworkSampler = hostNetworkSampler;
        _varIniPath = Environment.GetEnvironmentVariable("ARRDASH_UNRAID_VAR_INI") ?? "/var/local/emhttp/var.ini";
        _disksIniPath = Environment.GetEnvironmentVariable("ARRDASH_UNRAID_DISKS_INI") ?? "/var/local/emhttp/disks.ini";

        // var.ini/disks.ini are written by Unraid's emhttp daemon and don't exist on any other
        // Linux host — their presence is a reliable signal to gate Unraid-only concepts (parity,
        // mover, per-disk health) that have no equivalent on plain Docker/Linux.
        _isUnraidHost = File.Exists(_varIniPath) || File.Exists(_disksIniPath);

        _hostProcRootPath = Environment.GetEnvironmentVariable("ARRDASH_HOST_PROC_ROOT") ?? "/host/proc";
        _diskStatsPath = Environment.GetEnvironmentVariable("ARRDASH_DISKSTATS_PATH") ?? "/proc/diskstats";
        var dockerSocketPath = Environment.GetEnvironmentVariable("ARRDASH_DOCKER_SOCKET") ?? "/var/run/docker.sock";

        if (File.Exists(dockerSocketPath))
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(dockerSocketPath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };
            _dockerHttp = new HttpClient(handler) { BaseAddress = new Uri("http://docker") };
        }
    }

    public async Task<UnraidActivity?> ReadAsync(CancellationToken ct)
    {
        // Parity/mover/per-disk health are Unraid array concepts with no equivalent on plain
        // Linux — skip reading and reporting on them entirely rather than showing a perpetually
        // idle tile for a feature that doesn't exist on this host.
        var values = _isUnraidHost ? TryReadVarIni() : null;

        var parityRunning = values is not null && values.TryGetValue("mdResync", out var resync) && resync != "0";

        double? parityPercent = null;
        if (parityRunning &&
            values!.TryGetValue("mdResyncPos", out var posStr) && long.TryParse(posStr, out var pos) &&
            values.TryGetValue("mdResyncSize", out var sizeStr) && long.TryParse(sizeStr, out var size) &&
            size > 0)
        {
            parityPercent = Math.Clamp(pos * 100.0 / size, 0, 100);
        }

        var parityLabel = values is not null && values.TryGetValue("mdResyncAction", out var action)
            ? DescribeParityAction(action)
            : "Parity check";

        var moverRunning = values is not null &&
            values.TryGetValue("shareMoverActive", out var mover) &&
            mover.Equals("yes", StringComparison.OrdinalIgnoreCase);

        DateTimeOffset? moverStartedAt;
        lock (_moverLock)
        {
            if (moverRunning)
                _moverStartedAt ??= DateTimeOffset.UtcNow;
            else
                _moverStartedAt = null;

            moverStartedAt = _moverStartedAt;
        }

        IReadOnlyList<string> restarting = [];
        IReadOnlyList<string> recentlyUpdated = [];
        IReadOnlyList<ContainerUsage> topContainers = [];
        if (_dockerHttp is not null)
        {
            try
            {
                (restarting, recentlyUpdated) = await FetchContainerActivityAsync(ct);
            }
            catch
            {
                // Docker socket unreachable or API error — keep degrading silently.
            }

            try
            {
                topContainers = await GetTopContainersAsync(ct);
            }
            catch
            {
                // Same — no top-container data this cycle.
            }
        }

        var diskHealth = _isUnraidHost ? ReadDiskHealth() : null;
        var uptime = ReadUptime();
        var cpuTemp = ReadCpuTemperature();
        var cpuCoreTemps = ReadCpuCoreTemps();
        var network = _hostNetworkSampler.GetLatest();

        IReadOnlyList<StuckProcess> stuckProcesses = [];
        try
        {
            stuckProcesses = await ReadStuckProcessesAsync(ct);
        }
        catch
        {
            // No stuck-process data this cycle — not worth failing the whole read over.
        }

        var activity = new UnraidActivity(
            parityRunning, parityPercent, parityLabel, moverRunning, moverStartedAt,
            restarting, recentlyUpdated, diskHealth, topContainers, uptime, cpuTemp, cpuCoreTemps, network,
            _isUnraidHost, stuckProcesses);
        activity = activity with { IsUnraidHost = _isUnraidHost };

        var hasAnyRealData = values is not null || diskHealth is not null || topContainers.Count > 0 ||
            uptime is not null || cpuTemp is not null || network is not null;
        if (!hasAnyRealData)
            return null;

        return activity;
    }

    private Dictionary<string, string>? TryReadVarIni()
    {
        if (!File.Exists(_varIniPath))
            return null;

        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(_varIniPath))
            {
                var idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;

                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim().Trim('"');
                values[key] = value;
            }

            return values;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// disks.ini is INI-style with a [section] per disk (unlike var.ini's flat keys).
    /// status "DISK_NP_DSBL" means an unused parity slot, not a fault — normal on
    /// single-parity arrays. temp "*" means spun down / unavailable, not zero.
    /// </summary>
    /// <summary>
    /// Per-device "%util" (same metric iostat reports) computed from /proc/diskstats' cumulative
    /// "time spent doing I/Os" field, delta-tracked the same way CPU/network throughput are
    /// elsewhere in this class. Lets the Disks detail panel show which physical disk is actually
    /// busy when the CPU graph's "Waiting on disk" indicator is up — the CPU-side iowait% alone
    /// can't say which drive is responsible, but per-disk business can.
    /// </summary>
    private Dictionary<string, double> ReadDiskBusyPercentages()
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!File.Exists(_diskStatsPath))
            return result;

        var now = DateTimeOffset.UtcNow;
        lock (_diskIoLock)
        {
            foreach (var line in File.ReadLines(_diskStatsPath))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 13)
                    continue;

                var device = parts[2];
                if (!ulong.TryParse(parts[12], out var timeDoingIoMs))
                    continue;

                if (_prevDiskIo.TryGetValue(device, out var prev))
                {
                    var elapsedMs = (now - prev.At).TotalMilliseconds;
                    if (elapsedMs > 0)
                    {
                        var busyMs = timeDoingIoMs - prev.TimeDoingIoMs;
                        result[device] = Math.Clamp(busyMs / elapsedMs * 100.0, 0, 100);
                    }
                }

                _prevDiskIo[device] = (timeDoingIoMs, now);
            }
        }

        return result;
    }

    private DiskHealthSummary? ReadDiskHealth()
    {
        if (!File.Exists(_disksIniPath))
            return null;

        try
        {
            var sections = new List<Dictionary<string, string>>();
            Dictionary<string, string>? current = null;

            foreach (var line in File.ReadLines(_disksIniPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                    sections.Add(current);
                    continue;
                }

                if (current is null)
                    continue;

                var idx = trimmed.IndexOf('=');
                if (idx <= 0)
                    continue;

                var key = trimmed[..idx].Trim();
                var value = trimmed[(idx + 1)..].Trim().Trim('"');
                current[key] = value;
            }

            var unhealthy = new List<string>();
            var allDisks = new List<DiskInfo>();
            double? hottest = null;
            string? hottestName = null;
            var busyByDevice = ReadDiskBusyPercentages();

            foreach (var disk in sections)
            {
                if (!disk.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
                    continue;

                disk.TryGetValue("status", out var status);
                disk.TryGetValue("device", out var deviceName);

                // An empty parity slot (no device assigned, size 0) isn't a real disk — skip it
                // rather than list a phantom drive. Unraid reports this as DISK_NP_DSBL.
                if (status == "DISK_NP_DSBL" && string.IsNullOrWhiteSpace(deviceName))
                    continue;

                if (status is not ("DISK_OK" or "DISK_NP_DSBL"))
                    unhealthy.Add(name);

                double? temp = null;
                if (disk.TryGetValue("temp", out var tempStr) && tempStr != "*" &&
                    double.TryParse(tempStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTemp))
                {
                    temp = parsedTemp;
                    if (hottest is null || temp > hottest)
                    {
                        hottest = temp;
                        hottestName = name;
                    }
                }

                disk.TryGetValue("type", out var type);
                disk.TryGetValue("fsType", out var fsType);
                var spunDown = disk.TryGetValue("spundown", out var spundownStr) && spundownStr == "1";
                var rotational = disk.TryGetValue("rotational", out var rotationalStr) && rotationalStr == "1";
                var numErrors = disk.TryGetValue("numErrors", out var numErrorsStr) &&
                    int.TryParse(numErrorsStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedErrors)
                    ? parsedErrors
                    : 0;

                long? ParseKb(string key) =>
                    disk.TryGetValue(key, out var raw) &&
                    long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) && kb > 0
                        ? kb * 1024
                        : null;

                var busyPercent = !string.IsNullOrWhiteSpace(deviceName) && busyByDevice.TryGetValue(deviceName, out var busy)
                    ? busy
                    : (double?)null;

                allDisks.Add(new DiskInfo(
                    name,
                    type,
                    temp,
                    spunDown,
                    fsType,
                    ParseKb("size"),
                    ParseKb("fsUsed"),
                    ParseKb("fsFree"),
                    rotational,
                    status,
                    numErrors,
                    busyPercent));
            }

            return new DiskHealthSummary(unhealthy.Count == 0, unhealthy, hottest, hottestName, allDisks);
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan? ReadUptime()
    {
        const string path = "/proc/uptime";
        if (!File.Exists(path))
            return null;

        try
        {
            var content = File.ReadAllText(path);
            var firstToken = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return double.TryParse(firstToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? TimeSpan.FromSeconds(seconds)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadCpuTemperature()
    {
        const string basePath = "/sys/class/thermal";
        if (!Directory.Exists(basePath))
            return null;

        try
        {
            string? best = null;
            foreach (var zoneDir in Directory.GetDirectories(basePath, "thermal_zone*"))
            {
                var typePath = Path.Combine(zoneDir, "type");
                if (!File.Exists(typePath))
                    continue;

                var type = File.ReadAllText(typePath).Trim();
                if (type.Equals("x86_pkg_temp", StringComparison.OrdinalIgnoreCase))
                {
                    best = zoneDir;
                    break;
                }

                if (best is null && type.Equals("acpitz", StringComparison.OrdinalIgnoreCase))
                    best = zoneDir;
            }

            if (best is null)
                return null;

            var tempPath = Path.Combine(best, "temp");
            if (!File.Exists(tempPath))
                return null;

            var raw = File.ReadAllText(tempPath).Trim();
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliDegrees)
                ? milliDegrees / 1000.0
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Per-core temps from the "coretemp" hwmon device (labels like "Package id 0", "Core 0", "Core 4", ...).</summary>
    private static IReadOnlyList<CpuCoreTemp> ReadCpuCoreTemps()
    {
        const string basePath = "/sys/class/hwmon";
        if (!Directory.Exists(basePath))
            return [];

        try
        {
            string? coretempDir = null;
            foreach (var hwmonDir in Directory.GetDirectories(basePath, "hwmon*"))
            {
                var namePath = Path.Combine(hwmonDir, "name");
                if (File.Exists(namePath) && File.ReadAllText(namePath).Trim().Equals("coretemp", StringComparison.OrdinalIgnoreCase))
                {
                    coretempDir = hwmonDir;
                    break;
                }
            }

            if (coretempDir is null)
                return [];

            var temps = new List<CpuCoreTemp>();
            foreach (var labelPath in Directory.GetFiles(coretempDir, "temp*_label"))
            {
                var inputPath = labelPath.Replace("_label", "_input");
                if (!File.Exists(inputPath))
                    continue;

                var label = File.ReadAllText(labelPath).Trim();
                var raw = File.ReadAllText(inputPath).Trim();
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliDegrees))
                    temps.Add(new CpuCoreTemp(label, milliDegrees / 1000.0));
            }

            return temps
                .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    internal static (long Rx, long Tx)? ReadNetworkBytes(string procNetDevPath, string interfaceName)
    {
        if (!File.Exists(procNetDevPath))
            return null;

        try
        {
            foreach (var line in File.ReadLines(procNetDevPath))
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx <= 0)
                    continue;

                var name = line[..colonIdx].Trim();
                if (!string.Equals(name, interfaceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fields = line[(colonIdx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 9)
                    return null;

                return long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx) &&
                    long.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tx)
                    ? (rx, tx)
                    : null;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Forces the next GetTopContainersAsync call to fetch fresh data, used when the
    /// user actually opens the Memory/Top-CPU detail panel — the cache keeps the ambient tiles
    /// cheap, but a drill-down should never show data older than the click that triggered it.</summary>
    public void InvalidateTopContainerCache()
    {
        lock (_topContainerLock)
            _topContainerCachedAt = DateTimeOffset.MinValue;
    }

    public void InvalidateContainerNetworkCache()
    {
        lock (_containerNetworkLock)
            _prevContainerNetwork.Clear();
    }

    public async Task<(IReadOnlyList<ContainerNetworkRate> Rates, string? Note)> ReadContainerNetworkRatesAsync(CancellationToken ct)
    {
        if (_dockerHttp is null)
            return ([], "Docker socket unavailable — showing streaming sessions only.");

        var needsWarmup = false;
        lock (_containerNetworkLock)
            needsWarmup = _prevContainerNetwork.Count == 0;

        if (needsWarmup)
        {
            await SampleAndStoreContainerNetworkAsync(ct);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            }
            catch (OperationCanceledException)
            {
                return ([], null);
            }
        }

        return await ComputeContainerNetworkRatesAsync(ct);
    }

    private async Task<(IReadOnlyList<ContainerNetworkRate> Rates, string? Note)> ComputeContainerNetworkRatesAsync(CancellationToken ct)
    {
        var currentSamples = await ReadAllContainerNetworkSamplesAsync(ct);
        if (currentSamples.Count == 0)
            return ([], null);

        var now = DateTimeOffset.UtcNow;
        Dictionary<string, (long Rx, long Tx, DateTimeOffset At)> previous;
        lock (_containerNetworkLock)
        {
            previous = new Dictionary<string, (long Rx, long Tx, DateTimeOffset At)>(_prevContainerNetwork, StringComparer.Ordinal);
            _prevContainerNetwork = currentSamples.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.Rx, kv.Value.Tx, now),
                StringComparer.Ordinal);
        }

        if (previous.Count == 0)
            return ([], "Collecting container baseline — open again in a few seconds for per-container rates.");

        var rates = new List<ContainerNetworkRate>();
        foreach (var (name, current) in currentSamples)
        {
            if (!previous.TryGetValue(name, out var prev))
                continue;

            var elapsed = (now - prev.At).TotalSeconds;
            rates.Add(new ContainerNetworkRate(
                name,
                NetworkBandwidthBuilder.ComputeRate(current.Rx, prev.Rx, elapsed),
                NetworkBandwidthBuilder.ComputeRate(current.Tx, prev.Tx, elapsed)));
        }

        return (rates, null);
    }

    private async Task<Dictionary<string, (long Rx, long Tx)>> ReadAllContainerNetworkSamplesAsync(CancellationToken ct)
    {
        var containers = await FetchContainerIdsAndNamesAsync(ct);
        var currentSamples = new Dictionary<string, (long Rx, long Tx)>(StringComparer.Ordinal);
        if (containers.Count == 0)
            return currentSamples;

        using var throttle = new SemaphoreSlim(MaxConcurrentStatsCalls);
        var tasks = containers.Select(async entry =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await ReadContainerNetworkBytesAsync(entry.Id, entry.Name, ct);
            }
            catch
            {
                return null;
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        foreach (var sample in results.Where(s => s is not null))
            currentSamples[sample!.ContainerName] = (sample.RxBytes, sample.TxBytes);

        return currentSamples;
    }

    private async Task SampleAndStoreContainerNetworkAsync(CancellationToken ct)
    {
        var currentSamples = await ReadAllContainerNetworkSamplesAsync(ct);
        var now = DateTimeOffset.UtcNow;
        lock (_containerNetworkLock)
        {
            _prevContainerNetwork = currentSamples.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.Rx, kv.Value.Tx, now),
                StringComparer.Ordinal);
        }
    }

    private async Task<ContainerNetworkSample?> ReadContainerNetworkBytesAsync(string id, string name, CancellationToken ct)
    {
        using var response = await _dockerHttp!.GetAsync($"/containers/{id}/stats?stream=false", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var (rx, tx) = ParseContainerNetworkBytes(doc.RootElement);
        return new ContainerNetworkSample(name, rx, tx, DateTimeOffset.UtcNow);
    }

    internal static (long Rx, long Tx) ParseContainerNetworkBytes(JsonElement root)
    {
        if (!root.TryGetProperty("networks", out var networks) || networks.ValueKind != JsonValueKind.Object)
            return (0, 0);

        long rx = 0;
        long tx = 0;
        foreach (var network in networks.EnumerateObject())
        {
            if (network.Value.TryGetProperty("rx_bytes", out var rxEl))
                rx += rxEl.GetInt64();
            if (network.Value.TryGetProperty("tx_bytes", out var txEl))
                tx += txEl.GetInt64();
        }

        return (rx, tx);
    }

    // Public so the network bandwidth panel can attach per-container CPU% to its breakdown --
    // this reuses the same 20s-cached data that already drives the Top CPU tile rather than
    // triggering its own docker stats sampling for every container.
    public async Task<IReadOnlyList<ContainerUsage>> GetTopContainersAsync(CancellationToken ct)
    {
        lock (_topContainerLock)
        {
            if (_topContainersCached.Count > 0 && DateTimeOffset.UtcNow - _topContainerCachedAt < TopContainerCacheTtl)
                return _topContainersCached;
        }

        IReadOnlyList<ContainerUsage> result;
        try
        {
            result = await ComputeTopContainersAsync(ct);
        }
        catch
        {
            result = [];
        }

        lock (_topContainerLock)
        {
            _topContainersCached = result;
            _topContainerCachedAt = DateTimeOffset.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Processes in "D" state (uninterruptible sleep) are blocked on I/O so hard they can't even
    /// be signaled — a growing count is the clearest possible "storage itself is the problem"
    /// signal, distinct from general CPU iowait%. Best-effort resolves each stuck process back to
    /// the container it belongs to (via its cgroup path) so the dashboard can point at the cause,
    /// not just say "something's stuck."
    /// </summary>
    private async Task<IReadOnlyList<StuckProcess>> ReadStuckProcessesAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_hostProcRootPath))
            return [];

        string[] pidDirs;
        try
        {
            pidDirs = Directory.GetDirectories(_hostProcRootPath);
        }
        catch
        {
            return [];
        }

        List<(string Id, string Name)>? containers = null;
        var results = new List<StuckProcess>();

        foreach (var dir in pidDirs)
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid))
                continue;

            string stat;
            try
            {
                stat = await File.ReadAllTextAsync(Path.Combine(dir, "stat"), ct);
            }
            catch
            {
                continue;
            }

            // Format: "pid (comm) state ...". comm can itself contain spaces/parens, so find the
            // *last* ')' rather than splitting on whitespace.
            var openParen = stat.IndexOf('(');
            var closeParen = stat.LastIndexOf(')');
            if (openParen < 0 || closeParen <= openParen || closeParen + 2 >= stat.Length)
                continue;

            var state = stat[closeParen + 2];
            if (state != 'D')
                continue;

            var comm = stat[(openParen + 1)..closeParen];
            string? containerName = null;
            string? commandLine = null;

            try
            {
                var rawCmdline = await File.ReadAllTextAsync(Path.Combine(dir, "cmdline"), ct);
                commandLine = string.IsNullOrWhiteSpace(rawCmdline)
                    ? null
                    : rawCmdline.Replace('\0', ' ').Trim();
            }
            catch
            {
                // cmdline may be unreadable for kernel threads or restricted processes.
            }

            try
            {
                var cgroup = await File.ReadAllTextAsync(Path.Combine(dir, "cgroup"), ct);
                if (ExtractDockerContainerId(cgroup) is { } containerId)
                {
                    containers ??= _dockerHttp is not null
                        ? await FetchContainerIdsAndNamesAsync(ct)
                        : [];
                    containerName = containers
                        .FirstOrDefault(c => c.Id.StartsWith(containerId, StringComparison.Ordinal)).Name;
                }
            }
            catch
            {
                // No cgroup info available — fall back to the raw process name below.
            }

            results.Add(StuckProcessDisplay.Create(pid, comm, containerName, commandLine));
        }

        return results;
    }

    private static string? ExtractDockerContainerId(string cgroupContent)
    {
        foreach (var rawLine in cgroupContent.Split('\n'))
        {
            var line = rawLine.Trim();

            var dockerSlashIdx = line.IndexOf("/docker/", StringComparison.Ordinal);
            if (dockerSlashIdx >= 0)
            {
                var id = line[(dockerSlashIdx + "/docker/".Length)..].Split('/')[0];
                if (LooksLikeContainerId(id))
                    return id;
            }

            var scopeIdx = line.IndexOf("docker-", StringComparison.Ordinal);
            if (scopeIdx >= 0)
            {
                var rest = line[(scopeIdx + "docker-".Length)..];
                var scopeEnd = rest.IndexOf(".scope", StringComparison.Ordinal);
                var id = scopeEnd >= 0 ? rest[..scopeEnd] : rest;
                if (LooksLikeContainerId(id))
                    return id;
            }
        }

        return null;
    }

    private static bool LooksLikeContainerId(string value) =>
        value.Length >= 12 && value.All(Uri.IsHexDigit);

    private async Task<List<(string Id, string Name)>> FetchContainerIdsAndNamesAsync(CancellationToken ct)
    {
        using var listResponse = await _dockerHttp!.GetAsync("/containers/json", ct);
        listResponse.EnsureSuccessStatusCode();

        await using var listStream = await listResponse.Content.ReadAsStreamAsync(ct);
        using var listDoc = await JsonDocument.ParseAsync(listStream, cancellationToken: ct);

        var containers = new List<(string Id, string Name)>();
        if (listDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var container in listDoc.RootElement.EnumerateArray())
            {
                var id = container.TryGetProperty("Id", out var idEl) ? idEl.GetString() : null;
                var name = container.TryGetProperty("Names", out var namesEl) &&
                    namesEl.ValueKind == JsonValueKind.Array && namesEl.GetArrayLength() > 0
                    ? namesEl[0].GetString()?.TrimStart('/')
                    : null;

                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                    containers.Add((id, name));
            }
        }

        return containers;
    }

    private async Task<IReadOnlyList<ContainerUsage>> ComputeTopContainersAsync(CancellationToken ct)
    {
        var containers = await FetchContainerIdsAndNamesAsync(ct);

        using var throttle = new SemaphoreSlim(MaxConcurrentStatsCalls);
        var tasks = containers.Select(async entry =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await FetchContainerStatsAsync(entry.Id, entry.Name, ct);
            }
            catch
            {
                return null;
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results
            .Where(r => r is not null)
            .Cast<ContainerUsage>()
            .OrderByDescending(r => r.CpuPercent)
            .ToList();
    }

    private async Task<ContainerUsage?> FetchContainerStatsAsync(string id, string name, CancellationToken ct)
    {
        using var response = await _dockerHttp!.GetAsync($"/containers/{id}/stats?stream=false", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        if (!root.TryGetProperty("cpu_stats", out var cpuStats) || !root.TryGetProperty("precpu_stats", out var precpuStats))
            return null;

        var cpuUsage = cpuStats.GetProperty("cpu_usage");
        var cpuTotal = cpuUsage.GetProperty("total_usage").GetInt64();
        var preCpuTotal = precpuStats.TryGetProperty("cpu_usage", out var preUsage) &&
            preUsage.TryGetProperty("total_usage", out var preTotalEl)
            ? preTotalEl.GetInt64()
            : 0;

        var systemTotal = cpuStats.TryGetProperty("system_cpu_usage", out var sysEl) ? sysEl.GetInt64() : 0;
        var preSystemTotal = precpuStats.TryGetProperty("system_cpu_usage", out var preSysEl) ? preSysEl.GetInt64() : 0;

        var cpuDelta = cpuTotal - preCpuTotal;
        var systemDelta = systemTotal - preSystemTotal;

        var onlineCpus = cpuStats.TryGetProperty("online_cpus", out var onlineEl) && onlineEl.GetInt32() > 0
            ? onlineEl.GetInt32()
            : cpuUsage.TryGetProperty("percpu_usage", out var percpuEl) && percpuEl.ValueKind == JsonValueKind.Array
                ? Math.Max(percpuEl.GetArrayLength(), 1)
                : 1;

        // Docker's traditional per-container CPU% scales with core count (can exceed 100% up to
        // onlineCpus * 100), which isn't on the same scale as the host-wide aggregate gauge (0-100%
        // average across all cores) — comparing "1563%" to "18%" isn't meaningful at a glance.
        // CpuPercent here is normalized to the same 0-100 host-wide scale; CpuCores keeps the
        // traditional per-core-scaled figure as secondary "how many cores is this actually using" context.
        var hostSharePercent = systemDelta > 0 && cpuDelta > 0
            ? cpuDelta / (double)systemDelta * 100.0
            : 0;
        var cpuCores = hostSharePercent * onlineCpus / 100.0;

        double memPercent = 0;
        if (root.TryGetProperty("memory_stats", out var memStats) &&
            memStats.TryGetProperty("usage", out var usageEl) &&
            memStats.TryGetProperty("limit", out var limitEl) &&
            limitEl.GetInt64() > 0)
        {
            memPercent = usageEl.GetInt64() / (double)limitEl.GetInt64() * 100.0;
        }

        return new ContainerUsage(name, Math.Round(hostSharePercent, 1), Math.Round(cpuCores, 2), Math.Round(memPercent, 1));
    }

    /// <summary>
    /// Docker's "created" state just means "not started" — many containers with autostart
    /// disabled sit there indefinitely, so it is not evidence of an update in progress.
    /// Instead: "restarting" is reported as-is (a real, currently-observed transient state),
    /// and an actual update is inferred by noticing a running container's own Id change
    /// between polls (stop+recreate), which is reported for a short window after detection.
    /// </summary>
    private async Task<(IReadOnlyList<string> Restarting, IReadOnlyList<string> RecentlyUpdated)> FetchContainerActivityAsync(CancellationToken ct)
    {
        using var response = await _dockerHttp!.GetAsync("/containers/json?all=true", ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var restarting = new List<string>();
        var currentRunningIds = new Dictionary<string, string>(StringComparer.Ordinal);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var container in doc.RootElement.EnumerateArray())
            {
                var state = container.TryGetProperty("State", out var s) ? s.GetString() : null;
                var id = container.TryGetProperty("Id", out var idEl) ? idEl.GetString() : null;
                var name = container.TryGetProperty("Names", out var namesEl) &&
                    namesEl.ValueKind == JsonValueKind.Array &&
                    namesEl.GetArrayLength() > 0
                    ? namesEl[0].GetString()?.TrimStart('/')
                    : null;

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                    continue;

                if (state == "restarting")
                    restarting.Add(name);

                if (state == "running")
                    currentRunningIds[name] = id;
            }
        }

        var now = DateTimeOffset.UtcNow;
        lock (_containerLock)
        {
            foreach (var (name, id) in currentRunningIds)
            {
                if (_lastRunningIds.TryGetValue(name, out var previousId) && previousId != id)
                    _recentlyUpdated[name] = now;
            }

            _lastRunningIds = currentRunningIds;

            var stale = _recentlyUpdated
                .Where(kv => now - kv.Value > RecentlyUpdatedWindow)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var name in stale)
                _recentlyUpdated.Remove(name);

            return (restarting, _recentlyUpdated.Keys.ToList());
        }
    }

    private static string DescribeParityAction(string action)
    {
        if (action.Contains("recon", StringComparison.OrdinalIgnoreCase))
            return "Disk rebuild";
        if (action.Contains("check", StringComparison.OrdinalIgnoreCase))
            return "Parity check";
        return "Parity operation";
    }

    public void Dispose() => _dockerHttp?.Dispose();
}
