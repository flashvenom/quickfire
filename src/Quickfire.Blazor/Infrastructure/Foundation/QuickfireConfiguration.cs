using System.Collections;
using DotNetEnv;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace Quickfire.Blazor.Infrastructure.Foundation;

public static class QuickfireConfiguration
{
    private static readonly (string Legacy, string Canonical)[] Aliases =
    [
        // Normalize each source separately: deployment precedence is stronger than alias spelling.
        ("DOTNET_ENVIRONMENT", "environment"), ("ASPNETCORE_ENVIRONMENT", "environment"),
        ("QUICKFIRE_DB", "Database:Provider"), ("OPENFIRE_DB", "Database:Provider"),
        ("OPENFIRE_DESKTOP", "QUICKFIRE_DESKTOP"), ("OPENFIRE_DIR", "QUICKFIRE_DIR"),
        ("DEFAULTCONNECTION", "ConnectionStrings:DefaultConnection"),
        ("SYNCFUSION", "Syncfusion:LicenseKey"), ("ADMIN_EMAIL", "Admin:Email"),
        ("ADMIN_USERNAME", "Admin:Username"), ("ADMIN_PASSWORD", "Admin:Password"),
        ("ADMIN_FIRSTNAME", "Admin:FirstName"), ("ADMIN_LASTNAME", "Admin:LastName"),
        ("ADMIN_PICTURE", "Admin:Picture"),
        ("QUICKFIRE_FILESTORAGE_MODE", "FileStorage:Mode"),
        ("QUICKFIRE_FILESTORAGE_MAPPED_ROOT", "FileStorage:MappedRoot"),
        ("QUICKFIRE_FILESTORAGE_SERVER_ROOT", "FileStorage:ServerRoot"),
        ("QUICKFIRE_FILESTORAGE_PUBLIC_BASEURL", "FileStorage:PublicBaseUrl"),
        ("QUICKFIRE_FILESTORAGE_LOCAL_ROOT", "FileStorage:LocalRoot"),
        ("QUICKFIRE_FILESTORAGE_PREFER_FILE", "FileStorage:PreferFileLinks"),
        ("QUICKFIRE_FILESTORAGE_STRIP_UPLOADS", "FileStorage:StripUploadsFromMapped"),
        ("OPENFIRE_FILESTORAGE_MODE", "FileStorage:Mode"),
        ("OPENFIRE_FILESTORAGE_MAPPED_ROOT", "FileStorage:MappedRoot"),
        ("OPENFIRE_FILESTORAGE_SERVER_ROOT", "FileStorage:ServerRoot"),
        ("OPENFIRE_FILESTORAGE_PUBLIC_BASEURL", "FileStorage:PublicBaseUrl"),
        ("OPENFIRE_FILESTORAGE_LOCAL_ROOT", "FileStorage:LocalRoot"),
        ("OPENFIRE_FILESTORAGE_PREFER_FILE", "FileStorage:PreferFileLinks"),
        ("OPENFIRE_FILESTORAGE_STRIP_UPLOADS", "FileStorage:StripUploadsFromMapped")
    ];

    public static Dictionary<string, string?> Normalize(IEnumerable<KeyValuePair<string, string?>> values)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values) normalized[pair.Key.Replace("__", ":")] = pair.Value;
        foreach (var (legacy, canonical) in Aliases)
            if (!normalized.ContainsKey(canonical) && normalized.TryGetValue(legacy, out var value))
                normalized[canonical] = value;
        return normalized;
    }

    public static string ResolveContentRoot(string[] args, string? workingDirectory = null, string? applicationDirectory = null)
    {
        workingDirectory = Path.GetFullPath(workingDirectory ?? Directory.GetCurrentDirectory());
        applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
        var command = new ConfigurationBuilder().AddCommandLine(args).Build();
        var explicitRoot = command["contentRoot"] ?? Environment.GetEnvironmentVariable("DOTNET_CONTENTROOT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot, workingDirectory);

        // Recognize only normal build layouts. Published apps retain their own content root and .env.
        for (var ancestor = new DirectoryInfo(applicationDirectory); ancestor is not null; ancestor = ancestor.Parent)
        {
            var project = Path.Combine(ancestor.FullName, "src", "Quickfire.Blazor");
            if (File.Exists(Path.Combine(ancestor.FullName, "Quickfire.sln"))
                && File.Exists(Path.Combine(project, "Quickfire.Blazor.csproj"))
                && IsWithin(applicationDirectory, Path.Combine(ancestor.FullName, "build", "web"))) return project;
            if (File.Exists(Path.Combine(ancestor.FullName, "Quickfire.Blazor.csproj"))
                && IsWithin(applicationDirectory, Path.Combine(ancestor.FullName, "bin"))) return ancestor.FullName;
        }
        if (File.Exists(Path.Combine(applicationDirectory, "appsettings.json"))) return applicationDirectory;
        foreach (var candidate in new[] { workingDirectory, Path.Combine(workingDirectory, "src", "Quickfire.Blazor") })
            if (File.Exists(Path.Combine(candidate, "Quickfire.Blazor.csproj"))) return candidate;
        foreach (var candidate in new[] { workingDirectory, applicationDirectory })
            if (File.Exists(Path.Combine(candidate, "appsettings.json"))) return Path.GetFullPath(candidate);
        return workingDirectory;
    }

    private static bool IsWithin(string path, string directory)
        => path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var root = ResolveContentRoot(args);
        var envPath = Path.Combine(root, ".env");
        var local = File.Exists(envPath)
            ? Normalize(Env.NoEnvVars().NoClobber().Load(envPath).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            : new Dictionary<string, string?>();
        var process = Normalize(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(p => new KeyValuePair<string, string?>(p.Key.ToString()!, p.Value?.ToString())));
        var command = Normalize(new ConfigurationBuilder().AddCommandLine(args).Build().AsEnumerable());
        var bootstrap = new ConfigurationBuilder().AddInMemoryCollection(local).AddInMemoryCollection(process).AddInMemoryCollection(command).Build();
        var desktop = bootstrap["QUICKFIRE_DESKTOP"] == "1" || !string.IsNullOrWhiteSpace(bootstrap["QUICKFIRE_DIR"]);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args, ContentRootPath = root, ApplicationName = typeof(QuickfireConfiguration).Assembly.GetName().Name,
            EnvironmentName = bootstrap["environment"] ?? (desktop ? "Desktop" : null)
        });
        // Local defaults never overwrite deployment environment variables or command-line settings.
        var firstEnvironment = builder.Configuration.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource);
        builder.Configuration.Sources.Insert(firstEnvironment < 0 ? builder.Configuration.Sources.Count : firstEnvironment,
            new MemoryConfigurationSource { InitialData = local });
        builder.Configuration.AddInMemoryCollection(process).AddInMemoryCollection(command);
        return builder;
    }
}
