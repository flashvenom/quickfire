using Microsoft.AspNetCore.Builder;
using Quickfire.Blazor.Infrastructure.Foundation;

namespace Quickfire.Founation.Tests;

[CollectionDefinition("Configuration environment", DisableParallelization = true)]
public sealed class ConfigurationEnvironmentCollection { }

[Collection("Configuration environment")]
public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT")]
    public void Host_environment_uses_source_priority_then_dotnet_priority_and_preserves_desktop_startup(string localKey, string processKey)
    {
        using var environment = new ProcessEnvironment("DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT", "environment",
            "QUICKFIRE_DESKTOP", "QUICKFIRE_DIR", "OPENFIRE_DESKTOP", "OPENFIRE_DIR");
        using var fixture = new ConfigurationFixture();
        fixture.WriteEnv($"{localKey}=LocalFixture\n");
        Environment.SetEnvironmentVariable(processKey, "ProcessFixture");
        var builder = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.ProjectRoot]);
        try
        {
            Assert.Equal("ProcessFixture", builder.Environment.EnvironmentName);
            Assert.Equal("ProcessFixture", builder.Configuration["environment"]);
            Assert.Equal("ProcessFixture", Environment.GetEnvironmentVariable(processKey));
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }

        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "DotnetFixture");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "AspnetFixture");
        var sameSource = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.ProjectRoot]);
        try { Assert.Equal("DotnetFixture", sameSource.Environment.EnvironmentName); }
        finally { ((IDisposable)sameSource.Configuration).Dispose(); }
        var command = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.ProjectRoot, "--environment", "CommandFixture"]);
        try { Assert.Equal("CommandFixture", command.Environment.EnvironmentName); }
        finally { ((IDisposable)command.Configuration).Dispose(); }

        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Desktop");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Desktop");
        Environment.SetEnvironmentVariable("QUICKFIRE_DESKTOP", "1");
        var launchedDesktop = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.ProjectRoot]);
        try { Assert.Equal("Desktop", launchedDesktop.Environment.EnvironmentName); }
        finally { ((IDisposable)launchedDesktop.Configuration).Dispose(); }
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        fixture.WriteEnv("");
        var markerOnlyDesktop = QuickfireConfiguration.CreateBuilder(["--contentRoot", fixture.ProjectRoot]);
        try { Assert.Equal("Desktop", markerOnlyDesktop.Environment.EnvironmentName); }
        finally { ((IDisposable)markerOnlyDesktop.Configuration).Dispose(); }
    }

    [Theory]
    [InlineData("SYNCFUSION", "Syncfusion__LicenseKey")]
    [InlineData("Syncfusion__LicenseKey", "SYNCFUSION")]
    public void Process_license_overrides_local_alias_and_command_line_overrides_process(string localKey, string processKey)
    {
        using var environment = new ProcessEnvironment("SYNCFUSION", "Syncfusion__LicenseKey");
        using var fixture = new ConfigurationFixture();
        fixture.WriteEnv($"{localKey}=synthetic-local-license\n");
        Environment.SetEnvironmentVariable(processKey, "synthetic-process-license");
        var builder = fixture.CreateBuilder();
        try
        {
            Assert.Equal("synthetic-process-license", builder.Configuration["Syncfusion:LicenseKey"]);
            Assert.Equal("synthetic-process-license", Environment.GetEnvironmentVariable(processKey));
        }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
        var overridden = fixture.CreateBuilder("--Syncfusion:LicenseKey", "synthetic-command-license");
        try { Assert.Equal("synthetic-command-license", overridden.Configuration["Syncfusion:LicenseKey"]); }
        finally { ((IDisposable)overridden.Configuration).Dispose(); }
    }

    [Fact]
    public void Project_license_is_loaded_without_process_mutation_or_parent_env_traversal()
    {
        using var environment = new ProcessEnvironment("SYNCFUSION", "Syncfusion__LicenseKey");
        using var fixture = new ConfigurationFixture();
        File.WriteAllText(Path.Combine(fixture.Root, ".env"), "SYNCFUSION=synthetic-parent-license\n");
        var missing = fixture.CreateBuilder();
        try { Assert.Null(missing.Configuration["Syncfusion:LicenseKey"]); }
        finally { ((IDisposable)missing.Configuration).Dispose(); }
        fixture.WriteEnv("SYNCFUSION=synthetic-project-license\n");
        var local = fixture.CreateBuilder();
        try
        {
            Assert.Equal("synthetic-project-license", local.Configuration["Syncfusion:LicenseKey"]);
            Assert.Null(Environment.GetEnvironmentVariable("SYNCFUSION"));
        }
        finally { ((IDisposable)local.Configuration).Dispose(); }
    }

    [Theory]
    [InlineData("QUICKFIRE_DB", "OPENFIRE_DB")]
    [InlineData("OPENFIRE_DB", "QUICKFIRE_DB")]
    public void Provider_source_priority_is_preserved_across_old_and_new_names(string localKey, string processKey)
    {
        using var environment = new ProcessEnvironment("QUICKFIRE_DB", "OPENFIRE_DB", "Database__Provider");
        using var fixture = new ConfigurationFixture();
        fixture.WriteEnv($"{localKey}=Sqlite\n");
        Environment.SetEnvironmentVariable(processKey, "SqlServer");
        var builder = fixture.CreateBuilder();
        try { Assert.Equal("SqlServer", builder.Configuration["Database:Provider"]); }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
    }

    [Theory]
    [InlineData("MODE", "Mode")]
    [InlineData("MAPPED_ROOT", "MappedRoot")]
    [InlineData("SERVER_ROOT", "ServerRoot")]
    [InlineData("PUBLIC_BASEURL", "PublicBaseUrl")]
    [InlineData("LOCAL_ROOT", "LocalRoot")]
    [InlineData("PREFER_FILE", "PreferFileLinks")]
    [InlineData("STRIP_UPLOADS", "StripUploadsFromMapped")]
    public void Legacy_storage_env_is_read_and_current_name_wins_within_one_source(string suffix, string canonical)
    {
        var legacyKey = "OPENFIRE_FILESTORAGE_" + suffix;
        var currentKey = "QUICKFIRE_FILESTORAGE_" + suffix;
        using var environment = new ProcessEnvironment(legacyKey, currentKey, "FileStorage__" + canonical);
        using var fixture = new ConfigurationFixture();
        fixture.WriteEnv($"{legacyKey}=legacy-fixture-value\n");
        var builder = fixture.CreateBuilder();
        try { Assert.Equal("legacy-fixture-value", builder.Configuration["FileStorage:" + canonical]); }
        finally { ((IDisposable)builder.Configuration).Dispose(); }
        var normalized = QuickfireConfiguration.Normalize(new Dictionary<string, string?>
        {
            [legacyKey] = "legacy-fixture-value", [currentKey] = "current-fixture-value"
        });
        Assert.Equal("current-fixture-value", normalized["FileStorage:" + canonical]);
    }

    [Fact]
    public void Legacy_desktop_markers_are_mapped_without_changing_an_explicit_current_marker()
    {
        var legacy = QuickfireConfiguration.Normalize(new Dictionary<string, string?>
        {
            ["OPENFIRE_DESKTOP"] = "1", ["OPENFIRE_DIR"] = "legacy-directory", ["OPENFIRE_DB"] = "SqlServer"
        });
        Assert.Equal("1", legacy["QUICKFIRE_DESKTOP"]);
        Assert.Equal("legacy-directory", legacy["QUICKFIRE_DIR"]);
        Assert.Equal("SqlServer", legacy["Database:Provider"]);
        var current = QuickfireConfiguration.Normalize(new Dictionary<string, string?>
        {
            ["OPENFIRE_DESKTOP"] = "1", ["QUICKFIRE_DESKTOP"] = "0",
            ["OPENFIRE_DIR"] = "legacy-directory", ["QUICKFIRE_DIR"] = "current-directory",
            ["OPENFIRE_DB"] = "SqlServer", ["QUICKFIRE_DB"] = "Sqlite"
        });
        Assert.Equal("0", current["QUICKFIRE_DESKTOP"]);
        Assert.Equal("current-directory", current["QUICKFIRE_DIR"]);
        Assert.Equal("Sqlite", current["Database:Provider"]);
    }

    [Fact]
    public void Content_root_honors_command_then_dotnet_then_aspnetcore_host_configuration()
    {
        using var environment = new ProcessEnvironment("DOTNET_CONTENTROOT", "ASPNETCORE_CONTENTROOT");
        using var fixture = new ConfigurationFixture();
        var aspnetRoot = Path.Combine(fixture.Root, "aspnet");
        var dotnetRoot = Path.Combine(fixture.Root, "dotnet");
        Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", aspnetRoot);
        Assert.Equal(aspnetRoot, QuickfireConfiguration.ResolveContentRoot([]));
        Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", dotnetRoot);
        Assert.Equal(dotnetRoot, QuickfireConfiguration.ResolveContentRoot([]));
        Assert.Equal(fixture.ProjectRoot, QuickfireConfiguration.ResolveContentRoot(["--contentRoot", fixture.ProjectRoot]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_build_uses_its_own_project_even_from_an_unrelated_working_directory(bool projectBin)
    {
        using var environment = new ProcessEnvironment("DOTNET_CONTENTROOT", "ASPNETCORE_CONTENTROOT");
        using var fixture = new ConfigurationFixture();
        var unrelated = Path.Combine(fixture.Root, "unrelated");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "appsettings.json"), "{}");
        var output = projectBin ? Path.Combine(fixture.ProjectRoot, "bin", "Release", "net10.0")
            : Path.Combine(fixture.Root, "build", "web", "Release", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "appsettings.json"), "{}");
        Assert.Equal(fixture.ProjectRoot, QuickfireConfiguration.ResolveContentRoot([], unrelated, output));
    }

    [Fact]
    public void Published_app_uses_its_own_settings_and_source_repo_launch_resolves_web_project()
    {
        using var environment = new ProcessEnvironment("DOTNET_CONTENTROOT", "ASPNETCORE_CONTENTROOT");
        using var fixture = new ConfigurationFixture();
        var published = Path.Combine(fixture.Root, "artifacts", "published");
        Directory.CreateDirectory(published);
        File.WriteAllText(Path.Combine(published, "appsettings.json"), "{}");
        Assert.Equal(published, QuickfireConfiguration.ResolveContentRoot([], fixture.ProjectRoot, published));
        var noSettings = Path.Combine(fixture.Root, "other-output");
        Directory.CreateDirectory(noSettings);
        Assert.Equal(fixture.ProjectRoot, QuickfireConfiguration.ResolveContentRoot([], fixture.Root, noSettings));
        Assert.Equal(fixture.ProjectRoot, QuickfireConfiguration.ResolveContentRoot([], fixture.ProjectRoot, noSettings));
    }

    private sealed class ProcessEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _previous;
        public ProcessEnvironment(params string[] names)
        {
            _previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
        }
        public void Dispose()
        {
            foreach (var pair in _previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Quickfire.Configuration.Tests", Guid.NewGuid().ToString("N"));
        public string ProjectRoot => Path.Combine(Root, "src", "Quickfire.Blazor");
        public ConfigurationFixture()
        {
            Directory.CreateDirectory(ProjectRoot);
            File.WriteAllText(Path.Combine(Root, "Quickfire.sln"), "");
            File.WriteAllText(Path.Combine(ProjectRoot, "Quickfire.Blazor.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(ProjectRoot, "appsettings.json"), "{}");
        }
        public void WriteEnv(string values) => File.WriteAllText(Path.Combine(ProjectRoot, ".env"), values);
        public WebApplicationBuilder CreateBuilder(params string[] extra)
            => QuickfireConfiguration.CreateBuilder(["--contentRoot", ProjectRoot, "--environment", "Testing", .. extra]);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
