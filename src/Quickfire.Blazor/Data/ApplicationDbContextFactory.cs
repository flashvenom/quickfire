using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Quickfire.Blazor.Infrastructure.Foundation;

namespace Quickfire.Blazor.Data;

public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var builder = QuickfireConfiguration.CreateBuilder(args);
        var configuration = DatabaseConfiguration.Resolve(builder.Configuration, builder.Environment.ContentRootPath);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        if (!configuration.IsSqlite)
            throw new InvalidOperationException("This release maintains SQLite migrations only. SQL Server migrations require a separate provider-specific project.");
        options.UseSqlite(configuration.ConnectionString);
        return new ApplicationDbContext(options.Options);
    }
}
