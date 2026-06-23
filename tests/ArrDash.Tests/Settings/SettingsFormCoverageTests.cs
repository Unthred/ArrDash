using System.Text.RegularExpressions;
using ArrDash.Models;

namespace ArrDash.Tests.Settings;

/// <summary>
/// Static wiring audit: every user-facing setting should round-trip through Settings.razor
/// and be consumed by dashboard/runtime code.
/// </summary>
public class SettingsFormCoverageTests
{
    private static readonly string ProjectRoot = FindProjectRoot();
    private static readonly string SettingsRazor = Path.Combine(ProjectRoot, "src", "ArrDash", "Components", "Pages", "Settings.razor");

    public static IEnumerable<object[]> UserEditablePreferenceProperties()
    {
        yield return [nameof(UserLayoutPreferences.Theme)];
        yield return [nameof(UserLayoutPreferences.Density)];
        yield return [nameof(UserLayoutPreferences.BorderRadius)];
        yield return [nameof(UserLayoutPreferences.PrimaryColor)];
        yield return [nameof(UserLayoutPreferences.LightTextColor)];
        yield return [nameof(UserLayoutPreferences.DarkTextColor)];
        yield return [nameof(UserLayoutPreferences.LightBackgroundColor)];
        yield return [nameof(UserLayoutPreferences.DarkBackgroundColor)];
        yield return [nameof(UserLayoutPreferences.BackgroundStyle)];
        yield return [nameof(UserLayoutPreferences.BrandMark)];
        yield return [nameof(UserLayoutPreferences.DashboardTitle)];
        yield return [nameof(UserLayoutPreferences.DashboardSubtitle)];
        yield return [nameof(UserLayoutPreferences.HideHeroStrip)];
        yield return [nameof(UserLayoutPreferences.PosterSize)];
        yield return [nameof(UserLayoutPreferences.PosterPlacement)];
        yield return [nameof(UserLayoutPreferences.ShowQuality)];
        yield return [nameof(UserLayoutPreferences.ShowEpisodeBadges)];
        yield return [nameof(UserLayoutPreferences.ShowMissingEpisodes)];
        yield return [nameof(UserLayoutPreferences.ShowSyncNotes)];
        yield return [nameof(UserLayoutPreferences.PanelOrder)];
        yield return [nameof(UserLayoutPreferences.PanelCollapsed)];
        yield return [nameof(UserLayoutPreferences.PanelViewModes)];
        yield return [nameof(UserLayoutPreferences.PanelAccentColors)];
        yield return [nameof(UserLayoutPreferences.RecentWindowMode)];
        yield return [nameof(UserLayoutPreferences.RecentDays)];
        yield return [nameof(UserLayoutPreferences.DefaultRecentLimit)];
        yield return [nameof(UserLayoutPreferences.RecentLimits)];
        yield return [nameof(UserLayoutPreferences.AudiobookSource)];
        yield return [nameof(UserLayoutPreferences.TimeFormat)];
        yield return [nameof(UserLayoutPreferences.ShowPlexSessions)];
        yield return [nameof(UserLayoutPreferences.ShowEmbySessions)];
        yield return [nameof(UserLayoutPreferences.HideIdleSessions)];
        yield return [nameof(UserLayoutPreferences.ShowServerMetrics)];
        yield return [nameof(UserLayoutPreferences.MetricsPollIntervalSeconds)];
        yield return [nameof(UserLayoutPreferences.MetricsGraphWindowMinutes)];
        yield return [nameof(UserLayoutPreferences.EnableClickThrough)];
        yield return [nameof(UserLayoutPreferences.DeepLinkClickThrough)];
        yield return [nameof(UserLayoutPreferences.ExternalLinkTarget)];
        yield return [nameof(UserLayoutPreferences.MissingEpisodeClickAction)];
        yield return [nameof(UserLayoutPreferences.FriendlyQualityLabels)];
        yield return [nameof(UserLayoutPreferences.PollIntervalSeconds)];
        yield return [nameof(UserLayoutPreferences.ManualRefreshOnly)];
        yield return [nameof(UserLayoutPreferences.StatusBarMode)];
        yield return [nameof(UserLayoutPreferences.StartupPage)];
        yield return [nameof(UserLayoutPreferences.AutoKioskOnLoad)];
        yield return [nameof(UserLayoutPreferences.KioskHideHero)];
        yield return [nameof(UserLayoutPreferences.KioskLargeNowPlaying)];
        yield return [nameof(UserLayoutPreferences.KioskScreensaver)];
        yield return [nameof(UserLayoutPreferences.KioskScreensaverMinutes)];
        yield return [nameof(UserLayoutPreferences.KioskRotate)];
        yield return [nameof(UserLayoutPreferences.KioskRotateSeconds)];
        yield return [nameof(UserLayoutPreferences.ServiceEnabled)];
        yield return [nameof(UserLayoutPreferences.ShowSettingsHelp)];
    }

    [Theory]
    [MemberData(nameof(UserEditablePreferenceProperties))]
    public void Settings_form_loads_preference_property(string propertyName)
    {
        var loadPattern = new Regex($@"prefs\.{propertyName}\b", RegexOptions.CultureInvariant);
        var content = File.ReadAllText(SettingsRazor);

        Assert.True(loadPattern.IsMatch(content),
            $"Settings.razor LoadFromPrefs does not read prefs.{propertyName}.");
    }

    [Theory]
    [MemberData(nameof(UserEditablePreferenceProperties))]
    public void Settings_form_saves_preference_property(string propertyName)
    {
        var savePattern = new Regex($@"\bp\.{propertyName}\s*=", RegexOptions.CultureInvariant);
        var content = File.ReadAllText(SettingsRazor);

        Assert.True(savePattern.IsMatch(content),
            $"Settings.razor ApplyFormToPrefs does not assign p.{propertyName}.");
    }

    [Fact]
    public void Settings_form_preserves_button_color_on_save()
    {
        var content = File.ReadAllText(SettingsRazor);

        Assert.DoesNotMatch(@"p\.ButtonColor\s*=\s*""""", content);
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "src", "ArrDash")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate arrdash-blazor project root.");
    }
}
