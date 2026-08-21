using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// Reads tower infrastructure events from JSONL on the host (see docs/infrastructure-events.md).
/// </summary>
public sealed class InfrastructureEventsService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int MaxTailBytes = 512 * 1024;

    private readonly object _lock = new();
    private InfrastructureEventsSnapshot _snapshot = new([], [], [], DateTimeOffset.MinValue, null);
    private System.Threading.Timer? _timer;
    private readonly ILogger<InfrastructureEventsService> _logger;

    public InfrastructureEventsService(ILogger<InfrastructureEventsService> logger)
    {
        _logger = logger;
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.Zero, PollInterval);
    }

    public InfrastructureEventsSnapshot Current
    {
        get { lock (_lock) return _snapshot; }
    }

    public void Refresh()
    {
        try
        {
            var path = ResolveEventsPath();
            if (!File.Exists(path))
            {
                lock (_lock)
                    _snapshot = new([], [], [], DateTimeOffset.UtcNow, null);
                return;
            }

            var lines = ReadTailLines(path, MaxTailBytes);
            var events = new List<InfrastructureEventRecord>(lines.Count);
            foreach (var line in lines)
            {
                if (InfrastructureEventAggregator.TryParseLine(line, out var evt))
                    events.Add(evt);
            }

            var snapshot = InfrastructureEventAggregator.BuildSnapshot(events, DateTimeOffset.UtcNow);
            lock (_lock)
                _snapshot = snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed reading infrastructure events");
            lock (_lock)
                _snapshot = _snapshot with { ReadAt = DateTimeOffset.UtcNow, ReadError = ex.Message };
        }
    }

    internal static string ResolveEventsPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ARRDASH_INFRA_EVENTS_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        return "/mnt/cache/logs/server-events.jsonl";
    }

    private static IReadOnlyList<string> ReadTailLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var start = length > maxBytes ? length - maxBytes : 0;
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        if (start > 0)
            reader.ReadLine();
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines;
    }

    public void Dispose() => _timer?.Dispose();
}
