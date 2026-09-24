using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Quickfire.Blazor.Data;

namespace Quickfire.Blazor.Infrastructure.Foundation;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Quickfire.Database");
        if (!context.Database.IsSqlite())
        {
            logger.LogWarning("SQL Server is experimental. Automatic migration and bootstrap are supported only for SQLite; no SQLite migrations will be applied to SQL Server.");
            return;
        }
        var connection = new SqliteConnectionStringBuilder(context.Database.GetConnectionString());
        try
        {
            if (connection.DataSource != ":memory:" && connection.Mode != SqliteOpenMode.Memory)
            {
                var directory = Path.GetDirectoryName(connection.DataSource);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            }
            await context.Database.MigrateAsync(cancellationToken);
            SeedInitialData.SeedData(scope.ServiceProvider);
            logger.LogInformation("SQLite migrations and baseline initialization completed.");
        }
        catch (SqliteException ex)
        {
            // Do not interpret an unavailable, locked, or damaged database as a new installation.
            logger.LogError("SQLite initialization failed (code {Code}). Check the configured file, permissions and migration history; existing data was not replaced.", ex.SqliteErrorCode);
            throw new InvalidOperationException("SQLite initialization failed. Check file access and migration history, then restart. No fallback database was created.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException)
        {
            logger.LogError("SQLite initialization could not access or update the configured database. Check the service account's file permissions, available disk space and migration history; existing data was not replaced.");
            throw new InvalidOperationException("SQLite initialization failed. Check file permissions, disk space and migration history, then restart. No fallback database was created.");
        }
    }
}
