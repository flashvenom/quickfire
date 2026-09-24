#region Usings Statements
using DotNetEnv;
using Quickfire.Blazor.Data;
using Quickfire.Blazor.Domain.Attachments.Services;
using Quickfire.Blazor.Domain.Carriers.Services;
using Quickfire.Blazor.Domain.Clients.Services;
using Quickfire.Blazor.Domain.CompanyManual.Services;
using Quickfire.Blazor.Domain.Contacts.Services;
using Quickfire.Blazor.Domain.Ember;
using Quickfire.Blazor.Domain.Forms.Services;
using Quickfire.Blazor.Domain.Logs;
using Quickfire.Blazor.Domain.Policies.Services;
using Quickfire.Blazor.Domain.Renewals.Services;
using Quickfire.Blazor.Domain.Shared.Services;
using Quickfire.Blazor.Domain.Users.Services;
using Quickfire.Blazor.Interfaces;
using Quickfire.Blazor.Components.Account;
using Quickfire.Blazor.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components.Components.Tooltip;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Syncfusion.Blazor;
using Quickfire.Blazor.Infrastructure.Desktop;
#endregion

EnsureDesktopEnvironment();

// INITIAL VARIABLES -- -- -- -   -     -     -                -           -              -            -   -       -  -   -  - -  ---  -  -   -      -         -    -          -             /
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

bool environmentSaysDesktop = builder.Environment.IsEnvironment("Desktop");
bool desktopMarkersPresent = string.Equals(Environment.GetEnvironmentVariable("OPENFIRE_DESKTOP"), "1", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENFIRE_DIR"));
var contentRoot = builder.Environment.ContentRootPath ?? string.Empty;
bool runningFromDesktopOutput = contentRoot.Contains("\\build\\desktop\\", StringComparison.OrdinalIgnoreCase) || contentRoot.Contains("/build/desktop/", StringComparison.OrdinalIgnoreCase);
bool isDesktopRuntime = environmentSaysDesktop || desktopMarkersPresent || runningFromDesktopOutput;
if (isDesktopRuntime && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENFIRE_DIR")))
{
    Environment.SetEnvironmentVariable("OPENFIRE_DIR", ResolveDesktopDataDirectory());
}
DesktopAdminOptions desktopAdminOptions = BuildDesktopAdminOptions();
builder.Services.AddHttpClient();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddControllers();
Env.NoClobber().Load(Path.Combine(builder.Environment.ContentRootPath, ".env"));
bool detailedErrorsEnabled = builder.Configuration.GetValue<bool>("DetailedErrors:Enabled");

// IDEN AND AUTH -- -- -- -   -     -     -      -             -           -            -           -   -      -  -   -  --  ---  ---  -   -      -         -    -      -  -        idenauth/
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddAuthorization();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options => { options.DefaultScheme = IdentityConstants.ApplicationScheme; options.DefaultSignInScheme = IdentityConstants.ExternalScheme; }).AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager().AddDefaultTokenProviders();
 
// DATABASE  -- -- -- -   -     -     -      -             -           -            -            -   -      -  -   -  --  ---  -  -  -   -      -         -    -             -      database/
// Get connection string from config (environment variable can override for advanced scenarios, but not required)
string? configuredConnection = Environment.GetEnvironmentVariable("DEFAULTCONNECTION");
configuredConnection ??= builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=local.db";

// Get provider preference from config (environment variable can override for advanced scenarios, but not required)
string providerPreference = Environment.GetEnvironmentVariable("OPENFIRE_DB") ?? builder.Configuration["Database:Provider"] ?? (isDesktopRuntime ? "Sqlite" : "SqlServer");
bool useSqlite = string.Equals(providerPreference, "Sqlite", StringComparison.OrdinalIgnoreCase);
bool useSqlServer = string.Equals(providerPreference, "SqlServer", StringComparison.OrdinalIgnoreCase);

if (useSqlServer)
{
    // Use SQL Server - connection string from DefaultConnection
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    {
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        options.UseSqlServer(configuredConnection, sqlOptions =>
        {
            sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
            sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
        });
    });
}
else
{
    // Use SQLite (default) - connection string from DefaultConnection
    var sqliteConnection = PrepareSqliteConnectionString(
        configuredConnection, 
        Environment.GetEnvironmentVariable("OPENFIRE_DIR"),
        builder.Environment.ContentRootPath);

    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    {
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        options.UseSqlite(sqliteConnection, sqliteOptions =>
        {
            sqliteOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
        });
    });
}

builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

// COMPONENTS AND SYNCFUSION  -     -      -                -           -              -            -   -       -  -   -  - -  ---  --  -   -      -         -    -          -    sync fusion/
builder.Services.AddSyncfusionBlazor();
builder.Services.AddFluentUIComponents();
builder.Services.AddDataGridEntityFrameworkAdapter();
string? syncfusionLicense = Environment.GetEnvironmentVariable("SYNCFUSION");
if (!string.IsNullOrWhiteSpace(syncfusionLicense))
{
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionLicense);
}

// DEPENDENCIES -- -- -- -   -     -      -                -           -              -            -   -       -  -   -  - -  ---  --  -   -      -         -    -          -      injections/
builder.Services.AddScoped<AttachmentService>();
builder.Services.AddScoped<CarrierService>();
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<ClientStateService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<EmberService>();
builder.Services.AddScoped<FormService>();
builder.Services.AddScoped<FormsLibraryService>();
builder.Services.AddScoped<CompanyManualService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<PhoneLookupService>();
builder.Services.AddScoped<PolicyService>();
builder.Services.AddScoped<RenewalService>();
builder.Services.AddScoped<RenewalUpdateService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<AppJsInterop>();
builder.Services.AddScoped<SharedService>();
builder.Services.AddSingleton<IFileStorageResolver>(_ => FileStorageResolverAccessor.Resolver);
builder.Services.AddScoped<StateService>();
builder.Services.AddScoped<IDataReassignmentService, DataReassignmentService>();
builder.Services.AddScoped<SurefireDialogService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ILoggingService, LoggingService>();
builder.Services.AddScoped<ITooltipService, TooltipService>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB Max File Size
    hubOptions.StreamBufferCapacity = 100;
    hubOptions.MaximumParallelInvocationsPerClient = 10;
    #if DEBUG
    hubOptions.EnableDetailedErrors = true;
    #endif
});

// Misc -- -- -- -   -     - -- -- -- -   -  -     -            -                -            -   -       -  -   -  - -  ---  - -  -   -      -         -    -          -        -       misc/
builder.Services.AddHttpContextAccessor();
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true).AddEnvironmentVariables();
builder.Services.AddServerSideBlazor().AddHubOptions(o => { o.MaximumReceiveMessageSize = 102400000; });


// ------------------------------------------------------- -- -   -  -     -                                              
// App Configuration Protocols ----------------------- -- -   -  -     -      -      -           -     
// -------------------------------------------- --------- -- -   -  -     -             -                        -
Microsoft.AspNetCore.Builder.WebApplication app = builder.Build();
#if DEBUG
    app.UseDeveloperExceptionPage();
#endif

if (!isDesktopRuntime)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
else
{
    app.Logger.LogInformation("Desktop runtime detected: HTTPS enforcement disabled.");
    app.UseDeveloperExceptionPage();
}

app.UseMigrationsEndPoint();
app.MapStaticAssets();
// Configure static files that cache for 24 hours
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=86400");
    },
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

// Final config settings
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<EmberHub>("/emberHub");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapControllers();

await InitializeDatabaseIfFirstRunAsync(app.Services, app.Logger);

// Optional baseline seed for the Blazor server app when explicitly requested
if (ReadEnvFlag("OPENFIRE_SEED"))
{
    app.Logger.LogInformation("OPENFIRE_SEED detected; running baseline seed (admin, products, settings).");
    try
    {
        using var scope = app.Services.CreateScope();
        SeedInitialData.SeedData(scope.ServiceProvider);
        app.Logger.LogInformation("Baseline seed completed.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Baseline seed failed.");
        throw;
    }
}

if (isDesktopRuntime)
{
    await DesktopRuntimeInitializer.InitializeAsync(
        app.Services,
        new DesktopRuntimeOptions
        {
            Admin = desktopAdminOptions
        },
        app.Logger);
}

// -- -- -- -   -  -     -            -                -            -   -       -  -   -  - -  ---  - -  -   -      -         -    -          -        -          -              -         /

app.Run();

static void EnsureDesktopEnvironment()
{
    var hasDesktopMarker =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENFIRE_DESKTOP")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENFIRE_DIR"));

    if (hasDesktopMarker)
    {
        ForceEnvironment("Desktop");
    }
}

static void ForceEnvironment(string environmentName)
{
    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environmentName);
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environmentName);
}

static DesktopAdminOptions BuildDesktopAdminOptions()
{
    var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
    return new DesktopAdminOptions
    {
        Email = ReadEnvOrDefault("ADMIN_EMAIL", "admin@quickfire.local"),
        UserName = ReadEnvOrDefault("ADMIN_USERNAME", "admin@quickfire.local"),
        FirstName = ReadEnvOrDefault("ADMIN_FIRSTNAME", "Quickfire"),
        LastName = ReadEnvOrDefault("ADMIN_LASTNAME", "Admin"),
        TemporaryPassword = string.IsNullOrWhiteSpace(adminPassword) ? null : adminPassword.Trim(),
        PictureUrl = ReadEnvOrDefault("ADMIN_PICTURE", "default.jpg")
    };
}

static string PrepareSqliteConnectionString(string connectionString, string? dataDirectory, string contentRoot)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = "Data Source=local.db";
    }

    var builder = new SqliteConnectionStringBuilder(connectionString);

    if (!string.IsNullOrWhiteSpace(dataDirectory))
    {
        var absolute = Path.IsPathFullyQualified(dataDirectory)
            ? dataDirectory
            : Path.GetFullPath(Path.Combine(contentRoot, dataDirectory));

        Directory.CreateDirectory(absolute);
        
        // Extract just the filename if DataSource is a relative path, otherwise use the full path
        var dataSource = builder.DataSource;
        if (!Path.IsPathFullyQualified(dataSource))
        {
            // If relative, just use the filename
            dataSource = Path.GetFileName(dataSource);
        }
        else
        {
            // If absolute, extract just the filename to place in the data directory
            dataSource = Path.GetFileName(dataSource);
        }
        
        builder.DataSource = Path.Combine(absolute, dataSource);
    }
    else if (!Path.IsPathFullyQualified(builder.DataSource))
    {
        // If no data directory is specified and DataSource is relative, make it relative to content root
        var dbPath = Path.Combine(contentRoot, builder.DataSource);
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }
        builder.DataSource = dbPath;
    }

    // Set cache mode if not already specified
    if (!connectionString.Contains("Cache=", StringComparison.OrdinalIgnoreCase))
    {
        builder.Cache = SqliteCacheMode.Shared;
    }
    
    return builder.ToString();
}

static string ReadEnvOrDefault(string key, string defaultValue)
{
    var value = Environment.GetEnvironmentVariable(key);
    return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
}

static bool ReadEnvFlag(string key, bool defaultValue = false)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    var normalized = value.Trim();
    return normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
}

static async Task InitializeDatabaseIfFirstRunAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
{
    using var scope = services.CreateScope();
    var scopedProvider = scope.ServiceProvider;
    var context = scopedProvider.GetRequiredService<ApplicationDbContext>();

    var isFirstRun = await IsFirstRunAsync(context, logger, cancellationToken);
    if (!isFirstRun)
    {
        return;
    }

    logger.LogInformation("First run detected; applying migrations and seeding baseline data.");
    try
    {
        await context.Database.MigrateAsync(cancellationToken);
        SeedInitialData.SeedData(scopedProvider);
        logger.LogInformation("First run initialization completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "First run initialization failed.");
        throw;
    }
}

static async Task<bool> IsFirstRunAsync(ApplicationDbContext context, ILogger logger, CancellationToken cancellationToken = default)
{
    try
    {
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        return !appliedMigrations.Any();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Unable to read database migration history; assuming first run.");
        return true;
    }
}

static string ResolveDesktopDataDirectory()
{
    string baseRoot;
    if (OperatingSystem.IsWindows())
    {
        baseRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }
    else if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
    {
        var personal = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        baseRoot = Path.Combine(personal, "Library", "Application Support");
    }
    else
    {
        baseRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    var appRoot = Path.Combine(baseRoot, "flashvenom", "openfire");
    return Path.Combine(appRoot, "openfire-host", "data");
}
