namespace ArrDash.Services;

/// <summary>
/// Filters play events by Settings-selected libraries. Empty include list = all libraries.
/// Keys are <c>source:libraryExternalId</c> (e.g. <c>plex:1</c>).
/// </summary>
public static class WatchStatsLibraryFilter
{
    public static string Key(string source, string libraryExternalId) =>
        $"{source}:{libraryExternalId}";

    public static string PreferenceFingerprint(IReadOnlyList<string>? includedKeys)
    {
        if (includedKeys is null || includedKeys.Count == 0)
            return "all";

        var sorted = includedKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sorted.Length == 0)
            return "all";

        return string.Join(',', sorted);
    }

    public static IReadOnlyList<T> Apply<T>(
        IReadOnlyList<T> events,
        IReadOnlyList<string>? includedKeys,
        Func<T, string> sourceSelector,
        Func<T, string?> libraryExternalIdSelector)
    {
        if (includedKeys is null || includedKeys.Count == 0)
            return events;

        var set = new HashSet<string>(
            includedKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (set.Count == 0)
            return events;

        return events
            .Where(e =>
            {
                var libId = libraryExternalIdSelector(e);
                if (string.IsNullOrWhiteSpace(libId))
                    return false;
                return set.Contains(Key(sourceSelector(e), libId));
            })
            .ToList();
    }
}
