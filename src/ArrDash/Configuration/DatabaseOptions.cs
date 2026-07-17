namespace ArrDash.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "Sqlite";
    public string SqlitePath { get; set; } = "/config/arrdash.db";
    public string ConnectionString { get; set; } = "";

    public bool UsePostgres =>
        string.Equals(Provider, "Postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
}
