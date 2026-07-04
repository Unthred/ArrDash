using System.Collections.Concurrent;
using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed record AbsLibraryScan(string LibraryId, string Name, string Type);

public sealed class AudiobookShelfActivityTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AbsLibraryScan> _activeScans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _libraryNames = new(StringComparer.Ordinal);

    public bool IsConnected { get; private set; }

    public void SetConnected(bool connected) => IsConnected = connected;

    public void SetLibraryNames(IReadOnlyDictionary<string, string> names)
    {
        lock (_lock)
        {
            _libraryNames.Clear();
            foreach (var (id, name) in names)
                _libraryNames[id] = name;
        }
    }

    public void HandleInit(IReadOnlyList<string> libraryIds)
    {
        lock (_lock)
        {
            foreach (var id in libraryIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                _activeScans[id] = new AbsLibraryScan(id, ResolveName(id), "scan");
            }
        }
    }

    public void HandleScanStart(JsonElement scan)
    {
        if (!scan.TryGetProperty("id", out var idEl))
            return;

        var id = idEl.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return;

        var name = scan.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var type = scan.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "scan" : "scan";

        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _libraryNames[id] = name;

            _activeScans[id] = new AbsLibraryScan(id, ResolveName(id, name), type);
        }
    }

    public void HandleScanComplete(JsonElement scan)
    {
        if (!scan.TryGetProperty("id", out var idEl))
            return;

        var id = idEl.GetString();
        if (string.IsNullOrWhiteSpace(id))
            return;

        lock (_lock)
            _activeScans.Remove(id);
    }

    public void ClearScans()
    {
        lock (_lock)
            _activeScans.Clear();
    }

    public ServiceWorkload? GetCurrentWorkload()
    {
        AbsLibraryScan[] scans;
        lock (_lock)
            scans = _activeScans.Values.ToArray();

        if (scans.Length == 0)
            return null;

        if (scans.Length == 1)
            return ToWorkload(scans[0]);

        var labels = scans.Select(s => ToWorkload(s).Label).Distinct().ToArray();
        var hasMatch = scans.Any(s => string.Equals(s.Type, "match", StringComparison.OrdinalIgnoreCase));
        var kind = hasMatch ? ServiceWorkloadKind.Matching : ServiceWorkloadKind.Scanning;
        return new ServiceWorkload(kind, string.Join(" · ", labels), scans.Length);
    }

    internal IReadOnlyList<AbsLibraryScan> GetActiveScansForTests()
    {
        lock (_lock)
            return _activeScans.Values.ToList();
    }

    private string ResolveName(string libraryId, string? preferredName = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
            return preferredName;

        lock (_lock)
            return _libraryNames.TryGetValue(libraryId, out var name) ? name : libraryId;
    }

    private static ServiceWorkload ToWorkload(AbsLibraryScan scan)
    {
        var isMatch = string.Equals(scan.Type, "match", StringComparison.OrdinalIgnoreCase);
        var kind = isMatch ? ServiceWorkloadKind.Matching : ServiceWorkloadKind.Scanning;
        var verb = isMatch ? "Matching" : "Scanning";
        return new ServiceWorkload(kind, $"{verb} {scan.Name}", 1);
    }
}
