#region Usings Statements
using Quickfire.Blazor.Infrastructure.Foundation;
using Quickfire.Blazor.Infrastructure.Bridge;
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


// INITIAL VARIABLES -- -- -- -   -     -     -                -           -              -            -   -       -  -   -  - -  ---  -  -   -      -         -    -          -             /
WebApplicationBuilder builder = QuickfireConfiguration.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

bool environmentSaysDesktop = builder.Environment.IsEnvironment("Desktop");
bool desktopMarkersPresent = string.Equals(builder.Configuration["QUICKFIRE_DESKTOP"], "1", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(builder.Configuration["QUICKFIRE_DIR"]);
var contentRoot = builder.Environment.ContentRootPath ?? string.Empty;
bool runningFromDesktopOutput = contentRoot.Contains("\\build\\desktop\\", StringComparison.OrdinalIgnoreCase) || contentRoot.Contains("/build/desktop/", StringComparison.OrdinalIgnoreCase);
bool isDesktopRuntime = environmentSaysDesktop || desktopMarkersPresent || runningFromDesktopOutput;
if (isDesktopRuntime && string.IsNullOrWhiteSpace(builder.Configuration["QUICKFIRE_DIR"]))
{
    builder.Configuration["QUICKFIRE_DIR"] = ResolveDesktopDataDirectory();
}
builder.Services.AddHttpClient();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddControllers();
// Project-local .env was loaded by the configuration factory before settings were read.
// It never changes process environment values, preserving the deployment-first behavior of PR #15.
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
var databaseConfiguration = DatabaseConfiguration.Resolve(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    if (databaseConfiguration.IsSqlite)
        options.UseSqlite(databaseConfiguration.ConnectionString);
    else
        options.UseSqlServer(databaseConfiguration.ConnectionString, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

// COMPONENTS AND SYNCFUSION  -     -      -                -           -              -            -   -       -  -   -  - -  ---  --  -   -      -         -    -          -    sync fusion/
builder.Services.AddSyncfusionBlazor();
builder.Services.AddFluentUIComponents();
builder.Services.AddDataGridEntityFrameworkAdapter();
var syncfusionLicense = builder.Configuration["Syncfusion:LicenseKey"];
if (!string.IsNullOrWhiteSpace(syncfusionLicense))
    Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionLicense);

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
builder.Services.AddServerSideBlazor().AddHubOptions(o => { o.MaximumReceiveMessageSize = 102400000; });
builder.Services.AddQuickfireBridge();


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

if (app.Environment.IsDevelopment()) app.UseMigrationsEndPoint();
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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapQuickfireBridge();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();
app.MapControllers();

await DatabaseInitializer.InitializeAsync(app.Services);
app.Run();

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

    // Keep the existing desktop data location; renaming code must not select a fresh database.
    var appRoot = Path.Combine(baseRoot, "flashvenom", "openfire");
    return Path.Combine(appRoot, "openfire-host", "data");
}

// Exposed for isolated WebApplicationFactory integration tests.
public partial class Program { }
