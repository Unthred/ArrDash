using ArrDash.Models;

namespace ArrDash.Tests.Services;

public sealed class WatchStatsAliasTests
{
    [Fact]
    public void ParseAliases_reads_canonical_source_username_lines()
    {
        const string text = """
            Mom|plex|MargaretPlex
            Mom|emby|Margaret
            invalid-line
            Dad | jellyfin | Bob
            """;

        var aliases = ParseAliases(text);
        Assert.Equal(3, aliases.Count);
        Assert.Equal("Mom", aliases[0].CanonicalName);
        Assert.Equal("plex", aliases[0].Source);
        Assert.Equal("MargaretPlex", aliases[0].SourceUserName);
    }

    private static List<WatchStatsUserAlias> ParseAliases(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 3 && parts.All(p => !string.IsNullOrWhiteSpace(p)))
            .Select(parts => new WatchStatsUserAlias(parts[0], parts[1], parts[2]))
            .ToList();
    }
}
