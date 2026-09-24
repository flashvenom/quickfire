using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Quickfire.Blazor.Data;
using Quickfire.Blazor.Infrastructure.Foundation;

namespace Quickfire.Founation.Tests;

public sealed class FoundationTests
{
    [Fact]
    public async Task Fresh_setup_requires_password_then_resumes_without_resetting_accounts()
    {
        using var fixture = new DatabaseFixture();
        var missingPassword = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(fixture.Services));
        Assert.Contains("ADMIN_PASSWORD", missingPassword.Message);
        fixture.Configuration["Admin:Password"] = "Synthetic-first-run-72!";
        await DatabaseInitializer.InitializeAsync(fixture.Services);
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await context.Users.SingleAsync();
        var originalHash = account.PasswordHash;
        var products = await context.Products.CountAsync();
        var storage = (await context.Settings.SingleAsync()).FileStorage.ToJson();
        fixture.Configuration["Admin:Password"] = "Changed-config-must-not-reset-81!";
        await DatabaseInitializer.InitializeAsync(fixture.Services);
        context.ChangeTracker.Clear();
        Assert.Equal(originalHash, (await context.Users.SingleAsync()).PasswordHash);
        Assert.Equal(products, await context.Products.CountAsync());
        Assert.Equal(storage, (await context.Settings.SingleAsync()).FileStorage.ToJson());
        Assert.Contains(fixture.Root.Replace("\\", "\\\\"), storage);
        Assert.DoesNotContain("surefire.local", storage);
        Assert.DoesNotContain("S:", storage);
    }

    [Fact]
    public async Task Invalid_password_can_be_corrected_after_migrations()
    {
        using var fixture = new DatabaseFixture("short");
        await Assert.ThrowsAnyAsync<Exception>(() => DatabaseInitializer.InitializeAsync(fixture.Services));
        fixture.Configuration["Admin:Password"] = "Valid-after-retry-12!";
        await DatabaseInitializer.InitializeAsync(fixture.Services);
        using var scope = fixture.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData("20251223045234_InitializeDatabase")]
    [InlineData("20260206110613_AddFormsLibraryAndCompanyManual")]
    public async Task Existing_migration_history_and_rows_survive_upgrade(string migration)
    {
        using var fixture = new DatabaseFixture("Upgrade-fixture-94!");
        string? originalHash;
        const string existingStorage = """{"mode":1,"networkSharePath":"\\\\synthetic-server\\share","publicBaseUrl":"https://storage.example.invalid","serverAbsoluteRoot":"C:\\SyntheticStorage","stripUploadsFromMappedPath":true,"preferFileSchemeLinks":true}""";
        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.GetService<IMigrator>().MigrateAsync(migration);
            await context.Database.ExecuteSqlRawAsync("INSERT INTO Folders (Name, Description) VALUES ('Synthetic preserved folder', 'Upgrade fixture')");
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var existingUser = new ApplicationUser { UserName = "preserved@example.invalid", Email = "preserved@example.invalid", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(existingUser, "Existing-password-71!")).Succeeded);
            originalHash = existingUser.PasswordHash;
            // Use columns present in both historical schemas, before CompanyManualAdminUserId existed.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Settings (FileStore, FileStorageSettingsJson, FileServerMappedPath, DisablePlugins, SandbagMode, FakeyMode, OrganizationTimeZoneId) VALUES (1, {0}, {1}, 1, 0, 0, 'Pacific Standard Time')",
                existingStorage, @"\\synthetic-server\share");
        }
        await DatabaseInitializer.InitializeAsync(fixture.Services);
        // An upgrade with an existing account also works with no bootstrap password on restart.
        fixture.Configuration["Admin:Password"] = null;
        await DatabaseInitializer.InitializeAsync(fixture.Services);
        using var verify = fixture.Services.CreateScope();
        var upgraded = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Contains(migration, await upgraded.Database.GetAppliedMigrationsAsync());
        Assert.True(await upgraded.Folders.AnyAsync(x => x.Name == "Synthetic preserved folder"));
        Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        Assert.False(upgraded.Database.HasPendingModelChanges());
        var retainedUser = await upgraded.Users.SingleAsync();
        Assert.Equal("preserved@example.invalid", retainedUser.Email);
        Assert.Equal(originalHash, retainedUser.PasswordHash);
        Assert.True(await verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CheckPasswordAsync(retainedUser, "Existing-password-71!"));
        var retainedSettings = await upgraded.Settings.SingleAsync();
        Assert.Equal(Quickfire.Blazor.Domain.Shared.Models.FileStoreType.FileServer, retainedSettings.FileStore);
        Assert.Equal(existingStorage, retainedSettings.FileStorageSettingsJson);
        Assert.Equal(@"\\synthetic-server\share", retainedSettings.FileStorage.NetworkSharePath);
        Assert.Equal("https://storage.example.invalid", retainedSettings.FileStorage.PublicBaseUrl);
        Assert.Equal(@"\\synthetic-server\share", retainedSettings.FileServerMappedPath);
        Assert.True(retainedSettings.DisablePlugins);
    }

    [Fact]
    public void Invalid_provider_and_connection_never_fall_back()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Provider"] = "Typo" }).Build();
        Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.Resolve(config, Path.GetTempPath()));
        config["Database:Provider"] = "Sqlite";
        config["ConnectionStrings:DefaultConnection"] = "Server=private-host;Password=not-for-logs";
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.Resolve(config, Path.GetTempPath()));
        Assert.DoesNotContain("not-for-logs", error.ToString());
        config["Database:Provider"] = "SqlServer";
        Assert.Equal("SqlServer", DatabaseConfiguration.Resolve(config, Path.GetTempPath()).Provider);
    }

    [Fact]
    public void Process_configuration_wins_across_legacy_and_canonical_aliases()
    {
        var local = QuickfireConfiguration.Normalize(new Dictionary<string, string?> { ["OPENFIRE_DB"] = "Sqlite", ["DEFAULTCONNECTION"] = "Data Source=local.db" });
        var process = QuickfireConfiguration.Normalize(new Dictionary<string, string?> { ["Database__Provider"] = "SqlServer", ["ConnectionStrings__DefaultConnection"] = "Server=example;Database=sample;Integrated Security=true" });
        var config = new ConfigurationBuilder().AddInMemoryCollection(local).AddInMemoryCollection(process).Build();
        Assert.Equal("SqlServer", DatabaseConfiguration.Resolve(config, Path.GetTempPath()).Provider);
    }

    [Fact]
    public void Project_env_is_loaded_before_settings_without_mutating_process_environment()
    {
        using var fixture = new DatabaseFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), "{\"Database\":{\"Provider\":\"Sqlite\"}}");
        File.WriteAllText(Path.Combine(fixture.Root, ".env"), "OPENFIRE_DB=SqlServer\nADMIN_PASSWORD=Synthetic-env-fixture-39!\nDEFAULTCONNECTION=Server=example;Database=sample;Integrated Security=true\n");
        var previous = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        var builder = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.Root, "--environment", "Testing"]);
        Assert.Equal("SqlServer", builder.Configuration["Database:Provider"]);
        Assert.Equal(previous, Environment.GetEnvironmentVariable("ADMIN_PASSWORD"));
        Assert.Equal(fixture.Root, builder.Environment.ContentRootPath);
        var overridden = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.Root, "--environment", "Testing", "--OPENFIRE_DB", "Sqlite"]);
        Assert.Equal("Sqlite", overridden.Configuration["Database:Provider"]);
        Assert.Equal("Testing", overridden.Environment.EnvironmentName);
        (overridden.Configuration as IDisposable)?.Dispose();
        (builder.Configuration as IDisposable)?.Dispose();
    }

    [Fact]
    public void New_local_storage_produces_browser_links()
    {
        var storage = new Quickfire.Blazor.Domain.Shared.Models.FileStorageSettings
        {
            Mode = Quickfire.Blazor.Domain.Shared.Models.FileStorageMode.LocalDesktop,
            ServerAbsoluteRoot = Path.GetTempPath(), LocalRootPath = Path.GetTempPath(),
            PublicBaseUrl = null, PreferFileSchemeLinks = false
        };
        var resolver = new Quickfire.Blazor.Domain.Shared.Services.FileStorageResolver(storage);
        var attachment = new Quickfire.Blazor.Domain.Attachments.Models.Attachment
        { LocalPath = "uploads/fixture", HashedFileName = "synthetic document.pdf" };
        Assert.Equal("/uploads/fixture/synthetic%20document.pdf", resolver.BuildPublicUrl(attachment));
    }

    [Fact]
    public async Task Unavailable_database_is_not_replaced()
    {
        using var fixture = new DatabaseFixture("Unavailable-fixture-73!");
        File.WriteAllText(Path.Combine(fixture.Root, "fixture.db"), "Synthetic invalid SQLite file");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(fixture.Services));
        Assert.Equal("Synthetic invalid SQLite file", File.ReadAllText(Path.Combine(fixture.Root, "fixture.db")));
    }
}

internal sealed class DatabaseFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "Quickfire.Founation.Tests", Guid.NewGuid().ToString("N"));
    public IConfigurationRoot Configuration { get; }
    public ServiceProvider Services { get; }
    public DatabaseFixture(string? password = null)
    {
        Directory.CreateDirectory(Root);
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Admin:Password"] = password }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton<IWebHostEnvironment>(new TestEnvironment(Root));
        services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(Root, "fixture.db")};Pooling=False"));
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        Services = services.BuildServiceProvider();
    }
    public void Dispose()
    {
        Services.Dispose();
        Directory.Delete(Root, recursive: true);
    }
    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Quickfire.Blazor";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
