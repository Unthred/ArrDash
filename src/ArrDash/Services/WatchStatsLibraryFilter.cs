namespace ArrDash.Services;

/// <summary>
/// Hides play events from Settings-excluded libraries. Keys are <c>source:libraryExternalId</c>
/// (e.g. <c>plex:1</c>). Events without library info (Trakt, not-yet-enriched imports) always
/// stay visible — exclusion only ever hides libraries that are positively identified (#38).
/// </summary>
public static class WatchStatsLibraryFilter
{
    public static string Key(string source, string libraryExternalId) =>
        $"{source}:{libraryExternalId}";

    public static string PreferenceFingerprint(IReadOnlyList<string>? excludedKeys)
    {
        var set = NormalizedSet(excludedKeys);
        return set.Count == 0
            ? "none"
            : string.Join(',', set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<T> Apply<T>(
        IReadOnlyList<T> events,
        IReadOnlyList<string>? excludedKeys,
        Func<T, string> sourceSelector,
        Func<T, string?> libraryExternalIdSelector)
    {
        var excluded = NormalizedSet(excludedKeys);
        if (excluded.Count == 0)
            return events;

        return events
            .Where(e =>
            {
                var libId = libraryExternalIdSelector(e);
                if (string.IsNullOrWhiteSpace(libId))
                    return true;
                return !excluded.Contains(Key(sourceSelector(e), libId));
            })
            .ToList();
    }

    public static bool IsExcluded(
        IReadOnlyList<string>? excludedKeys,
        string source,
        string? libraryExternalId)
    {
        if (string.IsNullOrWhiteSpace(libraryExternalId))
            return false;
        return NormalizedSet(excludedKeys).Contains(Key(source, libraryExternalId));
    }

    private static HashSet<string> NormalizedSet(IReadOnlyList<string>? keys) =>
        keys is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : keys.Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
