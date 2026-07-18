using Microsoft.Extensions.Logging;

namespace ArrDash.Services;

/// <summary>
/// Applies the Settings-configured minimum log level to the running process.
///
/// Mutating <see cref="IConfiguration"/>'s indexer does NOT reliably raise a reload token
/// (confirmed empirically: <c>ConfigurationManager</c>'s live-update behavior covers adding new
/// sources, not setting values on the indexer), so <c>IOptionsMonitor&lt;LoggerFilterOptions&gt;</c>
/// never re-evaluates and the change silently never takes effect. Instead this reads/writes a
/// single mutable static (<see cref="DynamicLevel"/>), and Program.cs registers one
/// <c>ILoggingBuilder.AddFilter</c> closure at startup that consults it on every single log call
/// — filters are evaluated fresh each time, not cached, so a change here is live on the very
/// next log line with no reload machinery needed at all.
/// </summary>
public sealed class LogLevelService
{
    /// <summary>Null = inherit whatever appsettings.json / Logging__LogLevel__Default already set.</summary>
    public static LogLevel? DynamicLevel { get; private set; }

    private readonly LayoutPreferencesService _prefs;
    private readonly ILogger<LogLevelService> _logger;

    public LogLevelService(LayoutPreferencesService prefs, ILogger<LogLevelService> logger)
    {
        _prefs = prefs;
        _logger = logger;
        _prefs.Changed += Apply;
    }

    public void Apply()
    {
        var raw = _prefs.Current.LogLevel;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (DynamicLevel is null)
                return;
            DynamicLevel = null;
            _logger.LogInformation("Log level reset to server default");
            return;
        }

        if (!Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var parsed) || DynamicLevel == parsed)
            return;

        DynamicLevel = parsed;
        _logger.LogInformation("Log level set to {Level}", parsed);
    }
}
