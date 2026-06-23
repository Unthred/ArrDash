using System.Text.RegularExpressions;
using ArrDash.Models;
using ArrDash.Services;

namespace ArrDash.Tests.Settings;

public class ThemeBuilderTests
{
    [Fact]
    public void Build_uses_custom_background_and_surface_colors()
    {
        var prefs = new UserLayoutPreferences
        {
            LightBackgroundColor = "#abcdef",
            LightSurfaceColor = "#fedcba",
            DarkBackgroundColor = "#112233",
            DarkSurfaceColor = "#445566"
        };

        var theme = ThemeBuilder.Build(prefs);

        Assert.Equal("#abcdef", theme.PaletteLight!.Background);
        Assert.Equal("#fedcba", theme.PaletteLight.Surface);
        Assert.Equal("#112233", theme.PaletteDark!.Background);
        Assert.Equal("#445566", theme.PaletteDark.Surface);
    }

    [Fact]
    public void Theme_cache_key_includes_background_colors_so_appearance_preview_refreshes()
    {
        var baseline = new UserLayoutPreferences { PrimaryColor = "#6366f1" };
        var changedBackground = new UserLayoutPreferences
        {
            PrimaryColor = "#6366f1",
            LightBackgroundColor = "#ffffff"
        };

        Assert.NotEqual(
            ThemeBuilder.GetThemeCacheKey(baseline),
            ThemeBuilder.GetThemeCacheKey(changedBackground));
    }

    [Fact]
    public void Theme_cache_key_includes_gradient_colors_so_appearance_preview_refreshes()
    {
        var baseline = new UserLayoutPreferences { PrimaryColor = "#6366f1" };
        var changedGradient = new UserLayoutPreferences
        {
            PrimaryColor = "#6366f1",
            GradientStartColor = "#ff0000",
            GradientEndColor = "#00ff00"
        };

        Assert.NotEqual(
            ThemeBuilder.GetThemeCacheKey(baseline),
            ThemeBuilder.GetThemeCacheKey(changedGradient));
    }

    [Fact]
    public void ResolveButtonColor_uses_button_color_when_set()
    {
        var prefs = new UserLayoutPreferences
        {
            PrimaryColor = "#6366f1",
            ButtonColor = "#ff6600"
        };

        Assert.Equal("#ff6600", ThemeBuilder.ResolveButtonColor(prefs));
    }

    [Fact]
    public void GetRootStyle_includes_text_and_background_css_variables()
    {
        var prefs = new UserLayoutPreferences
        {
            GradientStartColor = "#111111",
            GradientEndColor = "#222222",
            LightBackgroundColor = "#333333",
            DarkBackgroundColor = "#444444",
            LightTextColor = "#010101",
            DarkTextColor = "#fefefe"
        };

        var lightStyle = ThemeBuilder.GetRootStyle(prefs, isDarkMode: false);
        var darkStyle = ThemeBuilder.GetRootStyle(prefs, isDarkMode: true);

        Assert.Contains("--arrdash-gradient-start:#111111", lightStyle);
        Assert.Contains("--arrdash-bg:#333333", lightStyle);
        Assert.Contains("--mud-palette-text-primary:#010101", lightStyle);
        Assert.Contains("--mud-palette-text-primary:#fefefe", darkStyle);
    }
}
