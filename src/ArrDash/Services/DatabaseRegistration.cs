using ArrDash.Configuration;
using ArrDash.Data;
using Microsoft.EntityFrameworkCore;

namespace ArrDash.Services;

public static class DatabaseRegistration
{
    public static IServiceCollection AddArrDashDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<WatchStatsOptions>(configuration.GetSection(WatchStatsOptions.SectionName));
        services.Configure<CleanupCandidatesOptions>(configuration.GetSection(CleanupCandidatesOptions.SectionName));

        services.AddDbContext<ArrDashDbContext>(options =>
        {
            var dbOptions = ResolveDatabaseOptions(configuration);
            if (dbOptions.UsePostgres && !string.IsNullOrWhiteSpace(dbOptions.ConnectionString))
                options.UseNpgsql(dbOptions.ConnectionString);
            else
            {
                var path = ResolveSqlitePath(dbOptions.SqlitePath);
                var dir = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(dir);
                options.UseSqlite($"Data Source={path}");
            }
        });

        services.AddDbContextFactory<ArrDashDbContext>();
        return services;
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArrDashDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSchemaUpgrader");
        await db.Database.EnsureCreatedAsync();
        await DatabaseSchemaUpgrader.UpgradeAsync(db, logger);
    }

    private static DatabaseOptions ResolveDatabaseOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        options.Provider = FirstNonEmpty(Environment.GetEnvironmentVariable("ARRDASH_DB_PROVIDER"), options.Provider);
        options.SqlitePath = FirstNonEmpty(Environment.GetEnvironmentVariable("ARRDASH_DB_SQLITE_PATH"), options.SqlitePath);
        options.ConnectionString = FirstNonEmpty(Environment.GetEnvironmentVariable("ARRDASH_DB_CONNECTION_STRING"), options.ConnectionString);
        return options;
    }

    private static string FirstNonEmpty(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static string ResolveSqlitePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        var configRoot = Environment.GetEnvironmentVariable("ARRDASH_CONFIG_PATH");
        return configRoot is not null
            ? Path.Combine(configRoot, configuredPath)
            : configuredPath;
    }
}
