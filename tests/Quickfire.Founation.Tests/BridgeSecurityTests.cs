using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Quickfire.Shared.Bridge;
using Quickfire.Blazor.Data;
using Quickfire.Blazor.Domain.Ember;
using Quickfire.Blazor.Infrastructure.Bridge;

namespace Quickfire.Founation.Tests;

public sealed class BridgeSecurityTests
{
    [Fact]
    public async Task CredentialsAreRandomHashedAndBoundToAnOwnerAndScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Devices.IssueAsync(fixture.Alice, "Alice's PC", BridgeScopes.Office);
        var second = await fixture.Devices.IssueAsync(fixture.Alice, "Phone integration", BridgeScopes.IncomingCall);
        Assert.NotEqual(first.Credential, second.Credential);
        Assert.Equal(32, first.Device.CredentialHash.Length);
        Assert.Equal(first.Device.IssuedUtc.AddDays(90), first.Device.ExpiresUtc);
        Assert.Equal("alice", (await fixture.Devices.AuthenticateAsync(first.Credential))!.UserId);
        Assert.Equal(BridgeScopes.IncomingCall, (await fixture.Devices.AuthenticateAsync(second.Credential))!.Scope);
        Assert.Null(await fixture.Devices.AuthenticateAsync(first.Credential[..33] + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Devices.IssueAsync(new ClaimsPrincipal(), "Anonymous", BridgeScopes.Office));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Devices.SelectAsync(fixture.Bob, first.Device.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Devices.RevokeAsync(fixture.Bob, first.Device.Id));
    }

    [Fact]
    public async Task RevokeAndExpiryAbortAnExistingConnectionAndRejectTheCredential()
    {
        await using var fixture = await Fixture.CreateAsync();
        var revoked = await fixture.Devices.IssueAsync(fixture.Alice, "Revoked", BridgeScopes.Office);
        var aborts = 0;
        fixture.Connections.Register(new(revoked.Device.Id, "alice", "revoked", () => aborts++));
        await fixture.Devices.RevokeAsync(fixture.Alice, revoked.Device.Id);
        Assert.Equal(1, aborts);
        Assert.Null(await fixture.Devices.AuthenticateAsync(revoked.Credential));
        var expired = await fixture.Devices.IssueAsync(fixture.Alice, "Expired", BridgeScopes.Office);
        fixture.Connections.Register(new(expired.Device.Id, "alice", "expired", () => aborts++));
        fixture.Clock.Advance(TimeSpan.FromDays(91));
        Assert.Null(await fixture.Devices.AuthenticateAsync(expired.Credential));
        Assert.Equal(2, aborts);
    }

    [Fact]
    public async Task OnlyFirstOfficePairingIsSelectedAndReplacementRequiresExplicitSelection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Devices.IssueAsync(fixture.Alice, "Calls first", BridgeScopes.IncomingCall);
        var first = await fixture.Devices.IssueAsync(fixture.Alice, "First Office", BridgeScopes.Office);
        var second = await fixture.Devices.IssueAsync(fixture.Alice, "Second Office", BridgeScopes.Office);
        Assert.True(first.Device.IsSelected);
        Assert.False(second.Device.IsSelected);
        await fixture.Devices.SelectAsync(fixture.Alice, second.Device.Id);
        await fixture.Devices.RevokeAsync(fixture.Alice, second.Device.Id);
        var replacement = await fixture.Devices.IssueAsync(fixture.Alice, "Replacement Office", BridgeScopes.Office);
        Assert.False(replacement.Device.IsSelected);
        Assert.Null(await fixture.Devices.GetSelectedAsync("alice"));
        await fixture.Devices.SelectAsync(fixture.Alice, replacement.Device.Id);
        Assert.Equal(replacement.Device.Id, (await fixture.Devices.GetSelectedAsync("alice"))!.Id);
    }

    [Fact]
    public async Task PasswordStampChangeInvalidatesBothHelperAndOldWebSession()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issued = await fixture.Devices.IssueAsync(fixture.Alice, "PC", BridgeScopes.Office);
        var aborted = false;
        fixture.Connections.Register(new(issued.Device.Id, "alice", "live", () => aborted = true));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var alice = await db.Users.SingleAsync(x => x.Id == "alice");
            alice.SecurityStamp = "changed";
            await db.SaveChangesAsync();
        }
        Assert.Null(await fixture.Devices.ValidateAsync(issued.Device.Id));
        Assert.True(aborted);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Devices.RequireUserAsync(fixture.Alice));
    }

    [Fact]
    public async Task SelectionDispatchesToExactlyOneConnectionAndResponsesAreCorrelated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Devices.IssueAsync(fixture.Alice, "Tray", BridgeScopes.Office);
        var second = await fixture.Devices.IssueAsync(fixture.Alice, "Desktop", BridgeScopes.Office);
        var bob = await fixture.Devices.IssueAsync(fixture.Bob, "Bob", BridgeScopes.Office);
        fixture.Connections.Register(new(first.Device.Id, "alice", "tray", () => { }));
        fixture.Connections.Register(new(second.Device.Id, "alice", "desktop", () => { }));
        fixture.Connections.Register(new(bob.Device.Id, "bob", "bob", () => { }));
        await fixture.Devices.SelectAsync(fixture.Alice, second.Device.Id);
        var clients = new RecordingClients();
        var dispatcher = fixture.Dispatcher(clients);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = dispatcher.DispatchAsync(fixture.Alice, "OutlookEmail_CreateNew", ["recipient@example.test"], cancellation.Token);
        var sent = await clients.Sent.Task.WaitAsync(cancellation.Token);
        Assert.Equal("desktop", sent.ConnectionId);
        Assert.Equal("ReceiveEmberCommand", sent.Method);
        Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(60), sent.Command.ExpiresUtc);
        Assert.Single(clients.Calls);
        var response = new BridgeResponse { RequestId = sent.Command.RequestId, Succeeded = true };
        await Assert.ThrowsAsync<HubException>(() => dispatcher.CompleteAsync(bob.Device, "bob", response));
        await Assert.ThrowsAsync<HubException>(() => dispatcher.CompleteAsync(second.Device, "other-connection", response));
        await dispatcher.CompleteAsync(second.Device, "desktop", response);
        Assert.True((await completion).Succeeded);
        await Assert.ThrowsAsync<HubException>(() => dispatcher.CompleteAsync(second.Device, "desktop", response));
    }

    [Fact]
    public async Task SwitchingSelectionRejectsThePreviousHelpersPendingResponse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Devices.IssueAsync(fixture.Alice, "Tray", BridgeScopes.Office);
        var second = await fixture.Devices.IssueAsync(fixture.Alice, "Desktop", BridgeScopes.Office);
        fixture.Connections.Register(new(first.Device.Id, "alice", "tray", () => { }));
        fixture.Connections.Register(new(second.Device.Id, "alice", "desktop", () => { }));
        var clients = new RecordingClients();
        var dispatcher = fixture.Dispatcher(clients);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completion = dispatcher.DispatchAsync(fixture.Alice, "GetWordDocContents", [], cancellation.Token);
        var sent = await clients.Sent.Task.WaitAsync(cancellation.Token);
        await fixture.Devices.SelectAsync(fixture.Alice, second.Device.Id);
        await Assert.ThrowsAsync<HubException>(() => dispatcher.CompleteAsync(first.Device, "tray", new BridgeResponse { RequestId = sent.Command.RequestId, Succeeded = true }));
        dispatcher.Disconnect(first.Device.Id, "tray");
        Assert.False((await completion).Succeeded);
    }

    [Fact]
    public async Task OfflineAndUnsupportedCommandsNeverDispatch()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Devices.IssueAsync(fixture.Alice, "Offline", BridgeScopes.Office);
        var clients = new RecordingClients();
        var dispatcher = fixture.Dispatcher(clients);
        Assert.False((await dispatcher.DispatchAsync(fixture.Alice, "Windows_OpenFile", ["document.pdf"])).Succeeded);
        Assert.False((await dispatcher.DispatchAsync(fixture.Alice, "RunShell", ["anything"])).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.DispatchAsync(new ClaimsPrincipal(), "Windows_OpenFile", []));
        Assert.Empty(clients.Calls);
    }

    [Fact]
    public async Task IncomingCallsReachOnlyTheOwnerAndExpiredBrowserSubscriptionsAreRemoved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var publisher = await fixture.Devices.IssueAsync(fixture.Alice, "Calls", BridgeScopes.IncomingCall);
        var office = await fixture.Devices.IssueAsync(fixture.Alice, "Office", BridgeScopes.Office);
        using var broker = new BridgeNotifications(fixture.Devices, NullLogger<BridgeNotifications>.Instance, fixture.Clock);
        var aliceEvents = 0;
        var bobEvents = 0;
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var aliceSubscription = broker.Subscribe(fixture.Alice, _ => { aliceEvents++; delivered.TrySetResult(); return Task.CompletedTask; });
        using var bobSubscription = broker.Subscribe(fixture.Bob, _ => { bobEvents++; return Task.CompletedTask; });
        var call = new CallInfo { CallerId = "5551234567", CallerName = "Synthetic caller" };
        await broker.PublishAsync(publisher.Device, call);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, aliceEvents);
        Assert.Equal(0, bobEvents);
        await Assert.ThrowsAsync<ArgumentException>(() => broker.PublishAsync(office.Device, call));
        await using (var db = fixture.Factory.CreateDbContext())
        {
            var alice = await db.Users.SingleAsync(x => x.Id == "alice");
            alice.SecurityStamp = "new-stamp";
            await db.SaveChangesAsync();
        }
        // A freshly paired publisher must not revive a browser session with the old stamp.
        var freshPublisher = await fixture.Devices.IssueAsync(Fixture.Principal("alice", "new-stamp"), "New calls", BridgeScopes.IncomingCall);
        var freshDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var freshSubscription = broker.Subscribe(Fixture.Principal("alice", "new-stamp"), _ => { freshDelivered.TrySetResult(); return Task.CompletedTask; });
        await broker.PublishAsync(freshPublisher.Device, call);
        await freshDelivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, aliceEvents);
        Assert.Equal(0, bobEvents);
    }

    [Fact]
    public async Task StalledBrowserDoesNotBlockAcceptanceOrOtherBrowsersAndQueuesStayBounded()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issued = await fixture.Devices.IssueAsync(fixture.Alice, "Calls", BridgeScopes.IncomingCall);
        using var broker = new BridgeNotifications(fixture.Devices, NullLogger<BridgeNotifications>.Instance, fixture.Clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fastDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stalledInvocations = 0;
        using var slow = broker.Subscribe(fixture.Alice, _ =>
        {
            Interlocked.Increment(ref stalledInvocations);
            entered.TrySetResult();
            return stalled.Task;
        });
        using var fast = broker.Subscribe(fixture.Alice, _ => { fastDelivered.TrySetResult(); return Task.CompletedTask; });
        var call = new CallInfo { CallerId = "5551234567", CallerName = "Synthetic caller" };
        await broker.PublishAsync(issued.Device, call).WaitAsync(TimeSpan.FromSeconds(3));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fastDelivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        // Overflow must drop events, not create one blocked callback/task per published call.
        for (var index = 0; index < 40; index++)
            await broker.PublishAsync(issued.Device, call).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, stalledInvocations);
        slow.Dispose();
        stalled.TrySetResult();
    }

    [Theory]
    [InlineData("https://example.test", "198.51.100.1", null, HttpStatusCode.Unauthorized)]
    [InlineData("https://example.test", "198.51.100.1", BridgeScopes.Office, HttpStatusCode.OK)]
    [InlineData("https://example.test", "198.51.100.1", BridgeScopes.IncomingCall, HttpStatusCode.Forbidden)]
    [InlineData("http://example.test", "127.0.0.1", BridgeScopes.Office, HttpStatusCode.Unauthorized)]
    [InlineData("http://localhost", "198.51.100.1", BridgeScopes.Office, HttpStatusCode.Unauthorized)]
    [InlineData("http://127.0.0.1", "127.0.0.1", BridgeScopes.Office, HttpStatusCode.OK)]
    [InlineData("http://localhost", "127.0.0.1", BridgeScopes.Office, HttpStatusCode.OK)]
    public async Task RealHubNegotiationEnforcesCredentialScopeAndTransport(string origin, string remoteIp, string? scope, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var host = fixture.Host(IPAddress.Parse(remoteIp));
        using var client = host.GetTestClient();
        client.BaseAddress = new Uri(origin);
        if (scope != null)
        {
            var issued = await fixture.Devices.IssueAsync(fixture.Alice, "Negotiate", scope);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Credential);
        }
        using var response = await client.PostAsync("/emberHub/negotiate?negotiateVersion=1", null);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Unauthorized)
        {
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("helper_pairing_required", body);
            Assert.Contains("Quickfire 1.2", body);
            Assert.Contains("Anonymous legacy helper connections are not supported", body);
        }
        if (expected == HttpStatusCode.Forbidden)
            Assert.Contains("helper_scope_mismatch", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RealHelperConnectionCompletesOnlyServerAssignedCommandsAndRevocationClosesIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var host = fixture.Host(IPAddress.Loopback);
        var devices = host.Services.GetRequiredService<BridgeDevices>();
        var issued = await devices.IssueAsync(fixture.Alice, "Synthetic native client", BridgeScopes.Office);
        await using var connection = new HubConnectionBuilder().WithUrl("https://localhost/emberHub", options =>
        {
            options.HttpMessageHandlerFactory = _ => host.GetTestServer().CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            options.AccessTokenProvider = () => Task.FromResult<string?>(issued.Credential);
        }).Build();
        var received = 0;
        connection.On<BridgeCommand>("ReceiveEmberCommand", async command =>
        {
            received++;
            await connection.InvokeAsync("CompleteCommand", new BridgeResponse
            {
                RequestId = command.RequestId, Succeeded = true, Data = ["Synthetic Word contents"]
            });
        });
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        await connection.StartAsync();
        // The SignalR handshake can finish before its asynchronous OnConnectedAsync registration.
        var registry = host.Services.GetRequiredService<BridgeConnections>();
        for (var attempt = 0; attempt < 100 && registry.Find(issued.Device.Id) == null; attempt++)
            await Task.Delay(10);
        Assert.NotNull(registry.Find(issued.Device.Id));
        var result = await host.Services.GetRequiredService<IBridgeDispatcher>().DispatchAsync(fixture.Alice, "GetWordDocContents", []);
        Assert.True(result.Succeeded);
        Assert.Equal("Synthetic Word contents", Assert.Single(result.Data));
        Assert.Equal(1, received);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinGroup", "bob"));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SendEmberCommand", "bob", "Windows_OpenFile", new List<string>()));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("CompleteCommand", new BridgeResponse { RequestId = result.RequestId, Succeeded = true }));
        await devices.RevokeAsync(fixture.Alice, issued.Device.Id);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(host.Services.GetRequiredService<BridgeConnections>().Snapshot());
    }

    [Fact]
    public async Task NativeCallWirePayloadIsOwnerBoundAndAcceptedWithoutWaitingForOfficeToast()
    {
        await using var fixture = await Fixture.CreateAsync();
        var blockedOffice = new SlowOfficeDispatcher();
        using var host = fixture.Host(IPAddress.Loopback, blockedOffice);
        var devices = host.Services.GetRequiredService<BridgeDevices>();
        var issued = await devices.IssueAsync(fixture.Alice, "Synthetic phone integration", BridgeScopes.IncomingCall);
        var office = await devices.IssueAsync(fixture.Alice, "Wrong-scope token", BridgeScopes.Office);
        using (var client = host.GetTestClient())
        {
            client.BaseAddress = new Uri("https://localhost");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", office.Credential);
            using var denied = await client.PostAsync("/notificationHub/negotiate?negotiateVersion=1", null);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        var aliceEvents = 0;
        var bobEvents = 0;
        var browserDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var broker = host.Services.GetRequiredService<BridgeNotifications>();
        using var alice = broker.Subscribe(fixture.Alice, call =>
        {
            Assert.Equal("5551234567", call.CallerId);
            Assert.Equal("Synthetic caller", call.CallerName);
            aliceEvents++;
            browserDelivered.TrySetResult();
            return Task.CompletedTask;
        });
        using var bob = broker.Subscribe(fixture.Bob, _ => { bobEvents++; return Task.CompletedTask; });
        await using var connection = new HubConnectionBuilder().WithUrl("https://localhost/notificationHub", options =>
        {
            options.HttpMessageHandlerFactory = _ => host.GetTestServer().CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
            options.AccessTokenProvider = () => Task.FromResult<string?>(issued.Credential);
        }).Build();
        await connection.StartAsync();
        // Matches Quickfire.Call's payload; an injected user ID cannot change server-derived ownership.
        await connection.InvokeAsync("SendIncomingCall", new
        {
            CallerId = "5551234567", CallerName = "Synthetic caller", UserId = "bob"
        }).WaitAsync(TimeSpan.FromSeconds(3));
        await blockedOffice.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await browserDelivered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, aliceEvents);
        Assert.Equal(0, bobEvents);
        Assert.False(blockedOffice.Finished);
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("CompleteCommand", new BridgeResponse { RequestId = Guid.NewGuid().ToString("N") }));
        await host.StopAsync();
        Assert.True(blockedOffice.Finished);
    }

    [Fact]
    public void ConcurrentRegistrationsLeaveOneExecutorAndDoNotAbortTheWinner()
    {
        var connections = new BridgeConnections();
        var id = Guid.NewGuid();
        var aborted = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        Parallel.For(0, 100, index =>
        {
            var connectionId = index.ToString();
            connections.Register(new(id, "alice", connectionId, () => aborted[connectionId] = true));
        });
        var winner = Assert.Single(connections.Snapshot());
        Assert.False(aborted.ContainsKey(winner.ConnectionId));
        Assert.Equal(99, aborted.Count);
        connections.Remove(id, "old-connection");
        Assert.Same(winner, connections.Find(id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly TestClock Clock = new();
        public readonly BridgeConnections Connections = new();
        public readonly ClaimsPrincipal Alice = Principal("alice", "alice-stamp");
        public readonly ClaimsPrincipal Bob = Principal("bob", "bob-stamp");
        public ContextFactory Factory { get; }
        public BridgeDevices Devices { get; }
        private string DatabasePath { get; }

        private Fixture(ContextFactory factory, string databasePath)
        {
            Factory = factory;
            DatabasePath = databasePath;
            Devices = new BridgeDevices(factory, Connections, Clock, Options.Create(new IdentityOptions()));
        }

        public static async Task<Fixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), "quickfire-bridge-" + Guid.NewGuid().ToString("N") + ".db");
            var factory = new ContextFactory(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=" + path).Options);
            var fixture = new Fixture(factory, path);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = "alice", UserName = "alice", SecurityStamp = "alice-stamp" },
                new ApplicationUser { Id = "bob", UserName = "bob", SecurityStamp = "bob-stamp" });
            await db.SaveChangesAsync();
            return fixture;
        }

        public BridgeDispatcher Dispatcher(RecordingClients clients) => new(Devices, Connections,
            new RecordingHub(clients), Clock, NullLogger<BridgeDispatcher>.Instance);

        public IHost Host(IPAddress remoteIp, IBridgeDispatcher? dispatcherOverride = null) => new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddRouting();
            services.Configure<IdentityOptions>(_ => { });
            services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(Factory);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddQuickfireBridge();
            if (dispatcherOverride != null) services.AddSingleton(dispatcherOverride);
        }).Configure(app =>
        {
            app.Use((context, next) => { context.Connection.RemoteIpAddress = remoteIp; return next(context); });
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapQuickfireBridge());
        })).Start();

        public static ClaimsPrincipal Principal(string id, string stamp) => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id), new Claim("AspNet.Identity.SecurityStamp", stamp)
        ], "TestIdentity"));

        public ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(DatabasePath);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed record SentCommand(string ConnectionId, string Method, BridgeCommand Command);

    private sealed class RecordingClients : IHubClients
    {
        public TaskCompletionSource<SentCommand> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<SentCommand> Calls { get; } = [];
        public IClientProxy Client(string connectionId) => new RecordingProxy(this, connectionId);
        public IClientProxy All => throw new InvalidOperationException("Broadcast is forbidden.");
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new InvalidOperationException("Group routing is forbidden.");
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        private sealed class RecordingProxy(RecordingClients owner, string connectionId) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                var sent = new SentCommand(connectionId, method, Assert.IsType<BridgeCommand>(Assert.Single(args)));
                owner.Calls.Add(sent);
                owner.Sent.TrySetResult(sent);
                return Task.CompletedTask;
            }
        }
    }

    private sealed class RecordingHub(RecordingClients clients) : IHubContext<EmberHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class SlowOfficeDispatcher : IBridgeDispatcher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Finished { get; private set; }
        public async Task<BridgeResponse> DispatchAsync(ClaimsPrincipal user, string command, List<string> parameters, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Finished = true; }
            return new BridgeResponse();
        }
    }
}
