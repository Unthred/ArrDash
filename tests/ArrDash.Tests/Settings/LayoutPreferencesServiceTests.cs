using System.Text.Json;
using System.Text.RegularExpressions;
using ArrDash.Models;
using ArrDash.Services;
using ArrDash.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArrDash.Tests.Settings;

public class LayoutPreferencesServiceTests : IDisposable
{
    private readonly string _configDir;
    private readonly LayoutPreferencesService _service;

    public LayoutPreferencesServiceTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "arrdash-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("ARRDASH_CONFIG_PATH", _configDir);

        _service = new LayoutPreferencesService(new FakeWebHostEnvironment(), NullLogger<LayoutPreferencesService>.Instance);
    }

    [Fact]
    public async Task Save_and_load_round_trip_preserves_all_preference_fields()
    {
        var original = PreferencesTestFactory.CreateFullyPopulated();
        await _service.SaveAsync(original);

        var reloaded = new LayoutPreferencesService(new FakeWebHostEnvironment(), NullLogger<LayoutPreferencesService>.Instance);
        await reloaded.LoadAsync();

        var left = JsonSerializer.Serialize(original);
        var right = JsonSerializer.Serialize(reloaded.Current);
        Assert.Equal(left, right);
    }

    [Fact]
    public async Task Preview_overrides_saved_preferences_until_cleared()
    {
        await _service.SaveAsync(new UserLayoutPreferences { DashboardTitle = "Saved" });

        var preview = new UserLayoutPreferences { DashboardTitle = "Preview" };
        _service.SetPreview(preview);

        Assert.Equal("Preview", _service.Current.DashboardTitle);
        Assert.Equal("Preview", _service.GetFormPreferences().DashboardTitle);

        _service.ClearPreview();
        Assert.Equal("Saved", _service.Current.DashboardTitle);
    }

    [Fact]
    public void GetRecentLimit_uses_default_limit_in_days_mode()
    {
        _service.SetPreview(new UserLayoutPreferences
        {
            RecentWindowMode = RecentWindowMode.Days,
            DefaultRecentLimit = 12,
            RecentLimits = { ["recent-tv"] = 50 }
        });

        Assert.Equal(12, _service.GetRecentLimit("recent-tv"));
    }

    [Fact]
    public void IsServiceEnabled_defaults_to_true_when_key_missing()
    {
        Assert.True(_service.IsServiceEnabled("sonarr"));
    }

    [Fact]
    public void IsServiceEnabled_respects_explicit_false()
    {
        _service.SetPreview(new UserLayoutPreferences
        {
            ServiceEnabled = { ["sonarr"] = false }
        });

        Assert.False(_service.IsServiceEnabled("sonarr"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ARRDASH_CONFIG_PATH", null);
        if (Directory.Exists(_configDir))
            Directory.Delete(_configDir, recursive: true);
    }
}
