using ArrDash.Models;
using Microsoft.JSInterop;

namespace ArrDash.Services;

public sealed class ThemeService(IJSRuntime js, LayoutPreferencesService prefs, ILogger<ThemeService> logger)
{
    public event Action? OnChange;

    public ThemePreference Preference => prefs.Current.Theme;

    public async Task<bool> ResolveDarkModeAsync()
    {
        var preference = prefs.Current.Theme;
        return preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => await GetSystemDarkAsync()
        };
    }

    public async Task SetPreferenceAsync(ThemePreference theme)
    {
        await prefs.UpdateThemeAsync(theme);
        OnChange?.Invoke();
    }

    public async Task RegisterSystemListenerAsync()
    {
        try
        {
            await js.InvokeVoidAsync("arrdashTheme.watchSystemPreference", DotNetObjectReference.Create(this));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "System theme listener unavailable");
        }
    }

    [JSInvokable]
    public Task OnSystemThemeChanged(bool isDark)
    {
        if (prefs.Current.Theme == ThemePreference.System)
            OnChange?.Invoke();
        return Task.CompletedTask;
    }

    private async Task<bool> GetSystemDarkAsync()
    {
        try
        {
            return await js.InvokeAsync<bool>("arrdashTheme.isSystemDark");
        }
        catch
        {
            return true;
        }
    }
}
