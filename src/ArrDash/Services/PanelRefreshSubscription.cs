using ArrDash.Models;

namespace ArrDash.Services;

/// <summary>
/// The one correct way for a panel with its own longer-TTL cache (Libraries, Chaptarr sync) to
/// react to dashboard-wide refresh events. <see cref="DashboardState.Changed"/> fires on every
/// automatic ~30s poll tick as well as on preference changes -- forcing a cache-bypassing
/// refresh there hammers Sonarr/Radarr/Chaptarr/ABS every tick instead of respecting the
/// panel's cache. Only <see cref="DashboardState.ManualRefreshRequested"/> (an explicit
/// "Refresh now" click) should force. Extracted so this wiring exists in exactly one place and
/// is unit-tested, after a bug where two panels each independently got it wrong.
/// </summary>
public sealed class PanelRefreshSubscription : IDisposable
{
    private readonly LayoutPreferencesService _prefs;
    private readonly DashboardState _dashState;
    private readonly Action<bool> _refresh;

    public PanelRefreshSubscription(LayoutPreferencesService prefs, DashboardState dashState, Action<bool> refresh)
    {
        _prefs = prefs;
        _dashState = dashState;
        _refresh = refresh;

        _prefs.Changed += OnPrefsChanged;
        _dashState.Changed += OnDashboardChanged;
        _dashState.ManualRefreshRequested += OnManualRefreshRequested;
    }

    private void OnPrefsChanged() => _refresh(false);
    private void OnDashboardChanged(DashboardSnapshot snapshot) => _refresh(false);
    private void OnManualRefreshRequested() => _refresh(true);

    public void Dispose()
    {
        _prefs.Changed -= OnPrefsChanged;
        _dashState.Changed -= OnDashboardChanged;
        _dashState.ManualRefreshRequested -= OnManualRefreshRequested;
    }
}
