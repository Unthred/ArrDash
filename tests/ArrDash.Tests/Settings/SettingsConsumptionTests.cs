using System.Text.RegularExpressions;
using ArrDash.Models;

namespace ArrDash.Tests.Settings;

/// <summary>
/// Ensures preference flags/parameters are actually honored by dashboard components.
/// </summary>
public class SettingsConsumptionTests
{
    private static readonly string ComponentsRoot = Path.Combine(FindProjectRoot(), "src", "ArrDash", "Components");

    [Fact]
    public void ShowMissingEpisodes_controls_episode_badge_rendering_in_download_panel()
    {
        var panel = ReadComponent("Panels/DownloadPanel.razor");

        Assert.Contains("[Parameter] public bool ShowMissingEpisodes", panel);
        Assert.Matches(
            @"ShowEpisodeBadges[\s\S]{0,200}EpisodeBadgeRow|EpisodeBadgeRow[\s\S]{0,200}ShowEpisodeBadges",
            panel);
        Assert.Matches(
            @"ShowMissingEpisodes[\s\S]{0,400}EpisodeBadgeRow|EpisodeBadgeRow[\s\S]{0,400}ShowMissingEpisodes",
            panel);
    }

    [Fact]
    public void ShowEpisodeBadges_can_disable_episode_badge_row_entirely()
    {
        var panel = ReadComponent("Panels/DownloadPanel.razor");

        Assert.Matches(@"@if\s*\(\s*ShowEpisodeBadges", panel);
    }

    [Fact]
    public void BackgroundStyle_setting_has_settings_ui()
    {
        var settings = ReadComponent("Pages/Settings.razor");

        Assert.Contains("BackgroundStyle", settings);
        Assert.Matches(@"_backgroundStyle|BackgroundStyle\s*\)", settings);
    }

    [Fact]
    public void Light_and_dark_background_colors_have_settings_ui()
    {
        var settings = ReadComponent("Pages/Settings.razor");

        Assert.Contains("LightBackgroundColor", settings);
        Assert.Contains("DarkBackgroundColor", settings);
    }

    [Fact]
    public void Panel_accent_colors_have_settings_ui()
    {
        var settings = ReadComponent("Pages/Settings.razor");

        Assert.Contains("PanelAccentColors", settings);
    }

    [Fact]
    public void FriendlyQualityLabels_is_used_when_formatting_quality_in_download_panel()
    {
        var panel = ReadComponent("Panels/DownloadPanel.razor");

        Assert.Matches(@"FormatQuality[\s\S]{0,120}FriendlyQualityLabels|FriendlyQualityLabels[\s\S]{0,120}FormatQuality", panel);
    }

    [Fact]
    public void DeepLinkClickThrough_is_used_when_resolving_item_urls()
    {
        var panel = ReadComponent("Panels/DownloadPanel.razor");

        Assert.Matches(@"ResolveUrl[\s\S]{0,200}DeepLinkClickThrough|DeepLinkClickThrough[\s\S]{0,200}ResolveUrl", panel);
    }

    [Fact]
    public void StartupPage_redirect_is_applied_when_preferences_change()
    {
        var home = ReadComponent("Pages/Home.razor");

        Assert.Contains("ApplyStartupPagePreference", home);
        Assert.Contains("StartupPage.Settings", home);
    }

    private static string ReadComponent(string relativePath) =>
        File.ReadAllText(Path.Combine(ComponentsRoot, relativePath));

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
