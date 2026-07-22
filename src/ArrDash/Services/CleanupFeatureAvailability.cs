using ArrDash.Configuration;

namespace ArrDash.Services;

/// <summary>
/// Cleanup needs Sonarr and/or Radarr inventory. Hide the feature when neither is usable.
/// </summary>
public sealed class CleanupFeatureAvailability(
    MediaServiceOptionsAccessor options,
    LayoutPreferencesService prefs)
{
    public bool IsAvailable
    {
        get
        {
            var media = options.Options;
            return (prefs.IsServiceEnabled("sonarr") && media.Sonarr.IsConfigured)
                || (prefs.IsServiceEnabled("radarr") && media.Radarr.IsConfigured);
        }
    }
}
