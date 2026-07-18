using ArrDash.Services;

namespace ArrDash.Tests.Services;

public class WatchStatsLibraryFilterTests
{
    private sealed record Ev(string Source, string? LibraryId);

    private static IReadOnlyList<Ev> Apply(IReadOnlyList<Ev> events, IReadOnlyList<string>? excluded) =>
        WatchStatsLibraryFilter.Apply(events, excluded, e => e.Source, e => e.LibraryId);

    [Fact]
    public void NoExclusions_ReturnsAllEvents()
    {
        var events = new[] { new Ev("plex", "1"), new Ev("trakt", null) };

        Assert.Equal(events, Apply(events, []));
        Assert.Equal(events, Apply(events, null));
    }

    [Fact]
    public void ExcludedLibrary_IsHidden()
    {
        var events = new[] { new Ev("plex", "1"), new Ev("plex", "2") };

        var result = Apply(events, ["plex:2"]);

        Assert.Equal([new Ev("plex", "1")], result);
    }

    [Fact]
    public void UnknownLibraryEvents_StayVisibleWhenExclusionsActive()
    {
        var events = new[] { new Ev("trakt", null), new Ev("emby", ""), new Ev("plex", "2") };

        var result = Apply(events, ["plex:2"]);

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(new Ev("plex", "2"), result);
    }

    [Fact]
    public void SameLibraryIdOnOtherSource_IsNotHidden()
    {
        var events = new[] { new Ev("plex", "5"), new Ev("emby", "5") };

        var result = Apply(events, ["plex:5"]);

        Assert.Equal([new Ev("emby", "5")], result);
    }

    [Fact]
    public void ExclusionKeys_AreCaseInsensitiveAndTrimmed()
    {
        var events = new[] { new Ev("plex", "abc") };

        Assert.Empty(Apply(events, [" PLEX:ABC "]));
    }

    [Fact]
    public void IsExcluded_UnknownLibrary_IsNeverExcluded()
    {
        Assert.False(WatchStatsLibraryFilter.IsExcluded(["plex:1"], "plex", null));
        Assert.False(WatchStatsLibraryFilter.IsExcluded(["plex:1"], "plex", ""));
        Assert.True(WatchStatsLibraryFilter.IsExcluded(["plex:1"], "plex", "1"));
        Assert.False(WatchStatsLibraryFilter.IsExcluded([], "plex", "1"));
    }

    [Fact]
    public void Fingerprint_IsStableAndOrderInsensitive()
    {
        Assert.Equal("none", WatchStatsLibraryFilter.PreferenceFingerprint([]));
        Assert.Equal("none", WatchStatsLibraryFilter.PreferenceFingerprint(null));
        Assert.Equal(
            WatchStatsLibraryFilter.PreferenceFingerprint(["emby:2", "plex:1"]),
            WatchStatsLibraryFilter.PreferenceFingerprint(["plex:1", "emby:2"]));
    }
}
