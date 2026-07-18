using System.Text.Json;
using ArrDash.Models;

namespace ArrDash.Services;

public sealed class DashboardState
{
    private readonly object _lock = new();
    private DashboardSnapshot _snapshot = Empty();

    public event Action<DashboardSnapshot>? Changed;

    // Fires only for an explicit user-initiated "Refresh now" -- separate from Changed, which
    // also fires on every automatic ~30s poll tick. Panels that keep their own longer-TTL cache
    // (Libraries, Chaptarr sync) must only force-bypass that cache on this event, not on Changed,
    // or they end up re-fetching from *arr/ABS every poll tick instead of respecting their cache.
    public event Action? ManualRefreshRequested;

    public DashboardSnapshot Current
    {
        get
        {
            lock (_lock)
                return _snapshot;
        }
    }

    public void Update(DashboardSnapshot snapshot)
    {
        Action<DashboardSnapshot>? handlers;
        lock (_lock)
        {
            _snapshot = snapshot;
            handlers = Changed;
        }

        handlers?.Invoke(snapshot);
    }

    public void NotifyManualRefresh() => ManualRefreshRequested?.Invoke();

    public static DashboardSnapshot Empty() => new(
        [],
        [],
        [],
        [],
        [],
        [],
        DateTimeOffset.UtcNow);
}

public sealed class LayoutPreferencesService(IWebHostEnvironment env, ILogger<LayoutPreferencesService> logger)
{
    private readonly string _path = Path.Combine(
        Environment.GetEnvironmentVariable("ARRDASH_CONFIG_PATH") ?? Path.Combine(env.ContentRootPath, "config"),
        "user-layout.json");
    private readonly object _lock = new();
    private UserLayoutPreferences _prefs = new();
    private UserLayoutPreferences? _preview;
    private UserLayoutPreferences? _settingsDraft;

    public event Action? Changed;

    public bool IsPreviewActive
    {
        get
        {
            lock (_lock)
                return _preview is not null;
        }
    }

    public UserLayoutPreferences Current
    {
        get
        {
            lock (_lock)
                return Clone(_preview ?? _prefs);
        }
    }

    public UserLayoutPreferences GetFormPreferences()
    {
        lock (_lock)
            return Clone(_settingsDraft ?? _preview ?? _prefs);
    }

    public void SetSettingsDraft(UserLayoutPreferences draft)
    {
        lock (_lock)
            _settingsDraft = Clone(draft);
    }

    public void ClearSettingsDraft()
    {
        lock (_lock)
            _settingsDraft = null;
    }

    public void SetPreview(UserLayoutPreferences preview)
    {
        lock (_lock)
            _preview = Clone(preview);
        Changed?.Invoke();
    }

    public void ClearPreview()
    {
        lock (_lock)
            _preview = null;
        Changed?.Invoke();
    }

    public bool DiffersFromSaved(UserLayoutPreferences candidate)
    {
        lock (_lock)
            return !PreferencesEqual(_prefs, candidate);
    }

    private static bool PreferencesEqual(UserLayoutPreferences left, UserLayoutPreferences right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var configDir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(configDir);

            if (!File.Exists(_path))
            {
                await SaveAsync(_prefs, ct);
                return;
            }

            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<UserLayoutPreferences>(stream, cancellationToken: ct);
            if (loaded is not null)
            {
                lock (_lock)
                    _prefs = loaded;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load layout preferences");
        }
    }

    public async Task SaveAsync(UserLayoutPreferences prefs, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _prefs = Clone(prefs);
            _preview = null;
            _settingsDraft = null;
        }

        var configDir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(configDir);

        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, _prefs, new JsonSerializerOptions { WriteIndented = true }, ct);
        Changed?.Invoke();
    }

    public async Task UpdateAsync(Action<UserLayoutPreferences> mutate, CancellationToken ct = default)
    {
        var current = Current;
        mutate(current);
        await SaveAsync(current, ct);
    }

    public int GetRecentLimit(string panelId) =>
        RecentItemFilter.ResolveLimit(Current, panelId);

    public bool IsPanelHidden(string panelId) =>
        Current.PanelCollapsed.TryGetValue(panelId, out var hidden) && hidden;

    public bool IsPanelRolledUp(string panelId) =>
        Current.PanelRolledUp.TryGetValue(panelId, out var rolled) && rolled;

    public async Task TogglePanelRollupAsync(string panelId, CancellationToken ct = default)
    {
        await UpdateAsync(p =>
        {
            var rolled = p.PanelRolledUp.TryGetValue(panelId, out var value) && value;
            p.PanelRolledUp[panelId] = !rolled;
        }, ct);
    }

    public bool IsServiceEnabled(string key) =>
        !Current.ServiceEnabled.TryGetValue(key, out var enabled) || enabled;

    public int GetPollIntervalSeconds(int envDefault) =>
        Current.PollIntervalSeconds > 0
            ? Math.Clamp(Current.PollIntervalSeconds, 10, 3600)
            : Math.Clamp(envDefault, 10, 3600);

    public int GetMetricsPollIntervalSeconds(int envDefault) =>
        Current.MetricsPollIntervalSeconds > 0
            ? Math.Clamp(Current.MetricsPollIntervalSeconds, 1, 60)
            : Math.Clamp(envDefault, 1, 60);

    public int GetMetricsGraphWindowMinutes(int envDefault) =>
        Current.MetricsGraphWindowMinutes > 0
            ? Math.Clamp(Current.MetricsGraphWindowMinutes, 5, 240)
            : Math.Clamp(envDefault, 5, 240);

    public IReadOnlyList<string> GetVisiblePanelOrder()
    {
        var prefs = Current;
        var known = PanelCatalog.DefaultOrder.ToHashSet(StringComparer.Ordinal);
        var order = prefs.PanelOrder.Where(known.Contains).ToList();
        foreach (var panelId in PanelCatalog.DefaultOrder)
        {
            if (!order.Contains(panelId))
                order.Add(panelId);
        }

        return order.Where(id => !IsPanelHidden(id)).ToList();
    }

    public string GetRootCssClasses(bool kiosk)
    {
        var prefs = Current;
        var classes = new List<string> { "arrdash-root" };
        if (kiosk)
            classes.Add("kiosk-root");

        classes.Add($"density-{prefs.Density.ToString().ToLowerInvariant()}");
        classes.Add($"bg-{prefs.BackgroundStyle.ToString().ToLowerInvariant()}");
        classes.Add($"radius-{prefs.BorderRadius.ToString().ToLowerInvariant()}");

        if (kiosk && prefs.KioskLargeNowPlaying)
            classes.Add("kiosk-large-now-playing");

        return string.Join(' ', classes);
    }

    public string GetRootStyle(bool isDarkMode) => ThemeBuilder.GetRootStyle(Current, isDarkMode);

    public string GetPanelAccent(string panelId, string fallback) =>
        Current.PanelAccentColors.TryGetValue(panelId, out var color) && !string.IsNullOrWhiteSpace(color)
            ? color
            : fallback;

    public DashboardDisplaySettings GetDisplaySettings()
    {
        var prefs = Current;
        return new DashboardDisplaySettings(
            string.IsNullOrWhiteSpace(prefs.DashboardTitle) ? "Your media universe" : prefs.DashboardTitle.Trim(),
            string.IsNullOrWhiteSpace(prefs.DashboardSubtitle)
                ? "Recent downloads and live playback across Sonarr, Radarr, Chaptarr, Plex, and Emby."
                : prefs.DashboardSubtitle.Trim(),
            prefs.PosterSize,
            prefs.PosterPlacement,
            new Dictionary<string, string>(prefs.PanelAccentColors));
    }

    public async Task UpdateThemeAsync(ThemePreference theme, CancellationToken ct = default)
    {
        var current = Current;
        current.Theme = theme;
        await SaveAsync(current, ct);
    }

    public async Task UpdatePanelOrderAsync(IReadOnlyList<string> order, CancellationToken ct = default)
    {
        var current = Current;
        current.PanelOrder = order.ToList();
        await SaveAsync(current, ct);
    }

    public async Task UpdateActivityLayoutAsync(IReadOnlyList<ActivityLayoutItem> layout, CancellationToken ct = default)
    {
        await UpdateAsync(p => p.ActivityLayout = layout
            .Select(item => new ActivityLayoutItem(item.Id, item.Span, item.Column))
            .ToList(), ct);
    }

    public async Task UpdatePanelViewModeAsync(string panelId, PanelViewMode mode, CancellationToken ct = default)
    {
        var current = Current;
        current.PanelViewModes[panelId] = mode;
        await SaveAsync(current, ct);
    }

    public async Task ToggleKioskAsync(bool enabled, CancellationToken ct = default)
    {
        var current = Current;
        current.KioskMode = enabled;
        await SaveAsync(current, ct);
    }

    // JSON round-trip so newly added preference fields can never be silently dropped:
    // the old field-by-field copy omitted every WatchStats* field, which reset them to
    // defaults on each read through Current/GetFormPreferences (#38).
    private static UserLayoutPreferences Clone(UserLayoutPreferences p) =>
        JsonSerializer.Deserialize<UserLayoutPreferences>(JsonSerializer.Serialize(p))!;
}
