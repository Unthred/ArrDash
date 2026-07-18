using ArrDash.Configuration;
using ArrDash.Models;
using MudBlazor;
using MudBlazor.Utilities;

namespace ArrDash.Services;

public static class ThemeBuilder
{
    // Pure static utility with no DI container of its own, called on every theme resolution
    // (hot path) — bootstrapped once from Program.cs rather than threading ILogger through
    // every method signature here and at every call site.
    private static ILogger? _logger;
    public static void Initialize(ILoggerFactory loggerFactory) => _logger ??= loggerFactory.CreateLogger(nameof(ThemeBuilder));

    public static MudTheme Build(UserLayoutPreferences prefs)
    {
        var primary = ResolveAccentColor(prefs);
        var secondary = NormalizeColor(prefs.SecondaryColor, "#0ea5e9");
        var button = ResolveButtonColor(prefs);
        var radius = prefs.BorderRadius == BorderRadiusStyle.Sharp ? "4px" : "14px";

        var lightBg = NormalizeColor(prefs.LightBackgroundColor, "#f4f6fb");
        var lightSurface = NormalizeColor(prefs.LightSurfaceColor, "#ffffff");
        var lightAppBar = NormalizeColor(prefs.LightAppBarColor, lightSurface);
        var darkBg = NormalizeColor(prefs.DarkBackgroundColor, "#0b1020");
        var darkSurface = NormalizeColor(prefs.DarkSurfaceColor, "#121829");
        var darkAppBar = NormalizeColor(prefs.DarkAppBarColor, darkSurface);
        var lightText = NormalizeColor(prefs.LightTextColor, "#0f172a");
        var darkText = NormalizeColor(prefs.DarkTextColor, "#eef2ff");

        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = button,
                PrimaryContrastText = ContrastText(button),
                Secondary = secondary,
                SecondaryContrastText = ContrastText(secondary),
                Background = lightBg,
                Surface = lightSurface,
                AppbarBackground = lightAppBar,
                AppbarText = lightText,
                TextPrimary = lightText,
                TextSecondary = "#64748b",
                Divider = "#e2e8f0",
                ActionDefault = "#64748b"
            },
            PaletteDark = new PaletteDark
            {
                Primary = button,
                PrimaryContrastText = ContrastText(button),
                Secondary = NormalizeColor(prefs.SecondaryColor, "#38bdf8"),
                SecondaryContrastText = ContrastText(NormalizeColor(prefs.SecondaryColor, "#38bdf8")),
                Background = darkBg,
                Surface = darkSurface,
                AppbarBackground = darkAppBar,
                AppbarText = darkText,
                TextPrimary = darkText,
                TextSecondary = "#94a3b8",
                Divider = "#1e293b",
                ActionDefault = "#94a3b8"
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = radius
            },
            Typography = new Typography
            {
                Default = new Default
                {
                    FontFamily = ["Inter", "Segoe UI", "system-ui", "sans-serif"]
                }
            }
        };
    }

    public static string GetRootStyle(UserLayoutPreferences prefs, bool isDarkMode)
    {
        var lightBg = NormalizeColor(prefs.LightBackgroundColor, "#f4f6fb");
        var darkBg = NormalizeColor(prefs.DarkBackgroundColor, "#0b1020");
        var lightSurface = NormalizeColor(prefs.LightSurfaceColor, "#ffffff");
        var darkSurface = NormalizeColor(prefs.DarkSurfaceColor, "#121829");
        var lightText = NormalizeColor(prefs.LightTextColor, "#0f172a");
        var darkText = NormalizeColor(prefs.DarkTextColor, "#eef2ff");
        var gradientStart = NormalizeColor(prefs.GradientStartColor, prefs.PrimaryColor, "#6366f1");
        var gradientEnd = NormalizeColor(prefs.GradientEndColor, prefs.SecondaryColor, "#0ea5e9");
        var bg = isDarkMode ? darkBg : lightBg;
        var surface = isDarkMode ? darkSurface : lightSurface;
        var textPrimary = isDarkMode ? darkText : lightText;
        var textSecondary = isDarkMode ? "#94a3b8" : "#64748b";
        var button = ResolveButtonColor(prefs);
        var accent = ResolveAccentColor(prefs);
        var vizSeries = isDarkMode ? VizSeriesDark : VizSeriesLight;
        var vizMuted = isDarkMode ? "#5a5a68" : "#9a9aa8";

        var vars = new List<string>
        {
            $"--arrdash-bg:{bg}",
            $"--arrdash-gradient-start:{gradientStart}",
            $"--arrdash-gradient-end:{gradientEnd}",
            $"--arrdash-accent:{accent}",
            $"--arrdash-button-fill:{button}",
            $"--arrdash-button-text:{ContrastText(button)}",
            $"--mud-palette-background:{bg}",
            $"--mud-palette-surface:{surface}",
            $"--mud-palette-text-primary:{textPrimary}",
            $"--mud-palette-text-secondary:{textSecondary}",
            $"--viz-muted:{vizMuted}"
        };
        for (var i = 0; i < vizSeries.Length; i++)
            vars.Add($"--viz-series-{i + 1}:{vizSeries[i]}");

        return string.Join(';', vars) + ";";
    }

    // Fixed categorical palette (dataviz skill reference instance) — order is the CVD-safety
    // mechanism (max adjacent ΔE), so slots must stay in this order, not be re-sorted per use.
    // Validated against this app's default light/dark surfaces via validate_palette.js.
    private static readonly string[] VizSeriesLight =
        ["#2a78d6", "#1baf7a", "#eda100", "#008300", "#4a3aa7", "#e34948", "#e87ba4", "#eb6834"];
    private static readonly string[] VizSeriesDark =
        ["#3987e5", "#199e70", "#c98500", "#008300", "#9085e9", "#e66767", "#d55181", "#d95926"];

    public static string ResolveAccentColor(UserLayoutPreferences prefs) =>
        NormalizeColor(prefs.PrimaryColor, "#6366f1");

    public static string ResolveButtonColor(UserLayoutPreferences prefs) =>
        string.IsNullOrWhiteSpace(prefs.ButtonColor)
            ? ResolveAccentColor(prefs)
            : NormalizeColor(prefs.ButtonColor, ResolveAccentColor(prefs));

    public static string DeriveSecondaryColor(string accentHex)
    {
        try
        {
            var accent = new MudColor(NormalizeColor(accentHex, "#6366f1"));
            var shift = accent.R + accent.G + accent.B > 382 ? -28 : 28;
            var r = (byte)Math.Clamp(accent.R + shift, 0, 255);
            var g = (byte)Math.Clamp(accent.G + shift / 2, 0, 255);
            var b = (byte)Math.Clamp(accent.B + shift, 0, 255);
            return new MudColor(r, g, b, (byte)255).ToString(MudColorOutputFormats.Hex);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "DeriveSecondaryColor failed for {AccentHex}", accentHex);
            return "#0ea5e9";
        }
    }

    public static string GetThemeCacheKey(UserLayoutPreferences prefs) =>
        string.Join('|',
            prefs.Theme,
            prefs.PrimaryColor,
            prefs.ButtonColor,
            prefs.SecondaryColor,
            prefs.LightTextColor,
            prefs.DarkTextColor,
            prefs.LightBackgroundColor,
            prefs.DarkBackgroundColor,
            prefs.LightSurfaceColor,
            prefs.DarkSurfaceColor,
            prefs.GradientStartColor,
            prefs.GradientEndColor,
            prefs.BackgroundStyle,
            prefs.BorderRadius,
            prefs.Density);

    public static string NormalizeColor(string? value, string fallback) =>
        NormalizeColor(value, null, fallback);

    private static string NormalizeColor(string? value, string? fallback2, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try { return new MudColor(value.Trim()).ToString(MudColorOutputFormats.Hex); }
            catch (Exception ex) { _logger?.LogDebug(ex, "NormalizeColor: invalid colour {Value}, trying fallback", value); }
        }

        if (!string.IsNullOrWhiteSpace(fallback2))
        {
            try { return new MudColor(fallback2.Trim()).ToString(MudColorOutputFormats.Hex); }
            catch (Exception ex) { _logger?.LogDebug(ex, "NormalizeColor: invalid fallback colour {Fallback}", fallback2); }
        }

        return fallback;
    }

    private static string ContrastText(string hex)
    {
        try
        {
            var c = new MudColor(hex);
            var brightness = (c.R * 299 + c.G * 587 + c.B * 114) / 1000.0;
            return brightness >= 128 ? "#0f172a" : "#ffffff";
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ContrastText failed for {Hex}", hex);
            return "#ffffff";
        }
    }
}
