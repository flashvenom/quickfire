using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Quickfire.Blazor.Data;
using Quickfire.Blazor.Domain.Clients.Services;
using Quickfire.Blazor.Domain.Home.Components;
using Quickfire.Blazor.Domain.Renewals.Services;
using Quickfire.Blazor.Domain.Shared.Services;

namespace Quickfire.Founation.Tests;

public sealed class MissingUserTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "deleted-account")]
    public async Task Missing_user_finishes_initialization_without_making_application_data_ready(bool authenticated, string? userId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.State.InitializeStateAsync(Authentication(authenticated, userId));
        await fixture.State.InitializationTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(fixture.State.IsInitialized);
        Assert.Null(fixture.State.CurrentUser);
        Assert.Null(fixture.State.UserPreferences);
        await using var database = fixture.Factory.CreateDbContext();
        Assert.Empty(await database.Settings.ToListAsync());
    }

    [Fact]
    public async Task Existing_user_still_initializes_preferences_and_updates_last_login()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = DateTime.UtcNow;
        await using (var database = fixture.Factory.CreateDbContext())
        {
            database.Users.Add(new ApplicationUser { Id = "existing-account", FirstName = "Existing", LastName = "User", EnableAudio = false });
            await database.SaveChangesAsync();
        }
        await fixture.State.InitializeStateAsync(Authentication(true, "existing-account"));

        Assert.True(fixture.State.IsInitialized);
        Assert.Equal("existing-account", fixture.State.CurrentUser?.Id);
        Assert.NotNull(fixture.State.UserPreferences);
        await using var verify = fixture.Factory.CreateDbContext();
        Assert.True((await verify.Users.SingleAsync()).LastLogin >= before);
    }

    [Fact]
    public async Task Daily_task_queries_return_no_data_without_an_application_user()
    {
        await using var fixture = await Fixture.CreateAsync();
        var home = new HomeService(fixture.State, fixture.Factory);
        var contextCount = fixture.Factory.ContextCount;

        Assert.Empty(await home.GetDailyTasksAsync());
        Assert.Empty(await home.GetDailyCompletedTasksAsync());
        Assert.Equal(contextCount, fixture.Factory.ContextCount);
    }

    [Fact]
    public async Task Daily_task_component_renders_no_greeting_or_task_actions_for_missing_user()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.State.InitializeStateAsync(Authentication(true, "deleted-account"));
        await using var database = fixture.Factory.CreateDbContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton(fixture.State);
        services.AddSingleton(new AppJsInterop(new NoJavaScript()));
        services.AddSingleton(new HomeService(fixture.State, fixture.Factory));
        services.AddSingleton(new TaskService(fixture.State, database, fixture.Factory));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var contextCount = fixture.Factory.ContextCount;

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<DailyTaskList>(ParameterView.Empty);
            return component.ToHtmlString();
        });

        Assert.DoesNotContain("Welcome back", html);
        Assert.DoesNotContain("newTaskInput", html);
        Assert.Equal(contextCount, fixture.Factory.ContextCount);
    }

    private static Task<AuthenticationState> Authentication(bool authenticated, string? userId)
    {
        Claim[] claims = userId is null ? [] : [new Claim(ClaimTypes.NameIdentifier, userId)];
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null))));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        public CountingFactory Factory { get; }
        public StateService State { get; }

        private Fixture(SqliteConnection connection)
        {
            this.connection = connection;
            provider = new ServiceCollection().BuildServiceProvider();
            Factory = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            State = new StateService(provider, Factory, new ConfigurationBuilder().Build(), new ClientStateService(new AppJsInterop(new NoJavaScript())));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await using var database = fixture.Factory.CreateDbContext();
            await database.Database.EnsureCreatedAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            State.Dispose();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class CountingFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public int ContextCount { get; private set; }
        public ApplicationDbContext CreateDbContext()
        {
            ContextCount++;
            return new ApplicationDbContext(options);
        }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Missing-user rendering must not initialize JavaScript controls.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://quickfire.example.test/", "https://quickfire.example.test/Home");
        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }
}
