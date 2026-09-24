using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;

namespace Quickfire.Blazor.Infrastructure.Foundation;

public sealed record DatabaseConfiguration(string Provider, string ConnectionString)
{
    public bool IsSqlite => Provider == "Sqlite";

    public static DatabaseConfiguration Resolve(IConfiguration configuration, string contentRoot)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connection = configuration.GetConnectionString("DefaultConnection");
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("SQL Server requires ConnectionStrings:DefaultConnection (or DEFAULTCONNECTION). No database was selected automatically.");
            try { _ = new SqlConnectionStringBuilder(connection); }
            catch (ArgumentException) { throw new InvalidOperationException("The configured SQL Server connection string is invalid. Check DEFAULTCONNECTION; its value has not been logged."); }
            return new("SqlServer", connection);
        }
        if (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Database:Provider (QUICKFIRE_DB) must be Sqlite or SqlServer. No fallback database was selected.");
        SqliteConnectionStringBuilder sqlite;
        try { sqlite = new SqliteConnectionStringBuilder(string.IsNullOrWhiteSpace(connection) ? "Data Source=local.db" : connection); }
        catch (ArgumentException) { throw new InvalidOperationException("The configured SQLite connection string is invalid. Check DEFAULTCONNECTION; its value has not been logged."); }
        if (string.IsNullOrWhiteSpace(sqlite.DataSource)) throw new InvalidOperationException("The SQLite connection string must specify Data Source.");
        if (sqlite.DataSource != ":memory:" && sqlite.Mode != SqliteOpenMode.Memory)
        {
            var directory = configuration["QUICKFIRE_DIR"];
            sqlite.DataSource = !string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Path.GetFullPath(directory, contentRoot), Path.GetFileName(sqlite.DataSource))
                : Path.GetFullPath(sqlite.DataSource, contentRoot);
        }
        if (!sqlite.ContainsKey("Cache")) sqlite.Cache = SqliteCacheMode.Shared;
        return new("Sqlite", sqlite.ToString());
    }
}
