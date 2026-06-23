using ArrDash.Models;
using ArrDash.Services;
using ArrDash.Tests.Infrastructure;

namespace ArrDash.Tests.Settings;

public class RecentItemFilterTests
{
    [Fact]
    public void Days_mode_filters_items_older_than_recent_days()
    {
        var prefs = new UserLayoutPreferences
        {
            RecentWindowMode = RecentWindowMode.Days,
            RecentDays = 7,
            DefaultRecentLimit = 100
        };

        var items = Enumerable.Range(0, 7)
            .Select(i => PreferencesTestFactory.CreateTvItem(i))
            .Concat([PreferencesTestFactory.CreateTvItem(10)])
            .ToList();

        var filtered = RecentItemFilter.Apply(items, prefs, "recent-tv");

        Assert.All(filtered, item => Assert.True(item.Timestamp >= DateTimeOffset.UtcNow.AddDays(-7)));
        Assert.Equal(7, filtered.Count);
    }

    [Fact]
    public void Days_mode_uses_default_limit_not_panel_limit()
    {
        var prefs = new UserLayoutPreferences
        {
            RecentWindowMode = RecentWindowMode.Days,
            RecentDays = 30,
            DefaultRecentLimit = 10,
            RecentLimits = { ["recent-tv"] = 50 }
        };

        var items = Enumerable.Range(0, 100)
            .Select(i => PreferencesTestFactory.CreateTvItem(daysAgo: i % 20))
            .ToList();

        var filtered = RecentItemFilter.Apply(items, prefs, "recent-tv");

        Assert.Equal(10, filtered.Count);
    }

    [Fact]
    public void Item_count_mode_uses_panel_specific_limit_when_set()
    {
        var prefs = new UserLayoutPreferences
        {
            RecentWindowMode = RecentWindowMode.ItemCount,
            DefaultRecentLimit = 10,
            RecentLimits = { ["recent-tv"] = 25 }
        };

        var items = Enumerable.Range(0, 100)
            .Select(i => PreferencesTestFactory.CreateTvItem(i))
            .ToList();

        var filtered = RecentItemFilter.Apply(items, prefs, "recent-tv");

        Assert.Equal(25, filtered.Count);
    }
}
