using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Quickfire.Shared.Bridge;
using Quickfire.Blazor.Domain.Ember;
using Quickfire.Blazor.Infrastructure.Bridge;

namespace Quickfire.Founation.Tests;

public sealed class WebBridgeTests
{
    [Fact]
    public async Task AnonymousUserCannotDispatchDesktopCommands()
    {
        var auth = new TestAuthenticationStateProvider();
        var dispatcher = new RecordingDispatcher();
        var service = CreateService(auth, dispatcher);

        Assert.False(await service.RunEmberFunction("Windows_OpenFolder", ["test-folder"]));
        Assert.Empty(dispatcher.Users);
    }

    [Fact]
    public async Task EachActionUsesTheCurrentIdentityRatherThanACachedDesktopUsername()
    {
        var auth = new TestAuthenticationStateProvider("first-user");
        var dispatcher = new RecordingDispatcher { Succeeded = true };
        var service = CreateService(auth, dispatcher);

        Assert.True(await service.RunEmberFunction("OutlookSearch_EmailBroad", ["first@example.test"]));
        auth.SignIn("second-user");
        Assert.True(await service.RunEmberFunction("OutlookSearch_EmailBroad", ["second@example.test"]));

        Assert.Equal(["first-user", "second-user"], dispatcher.Users);
        Assert.Equal(2, auth.ReadCount);
    }

    [Fact]
    public async Task SigningOutStopsSubsequentDispatch()
    {
        var auth = new TestAuthenticationStateProvider("first-user");
        var dispatcher = new RecordingDispatcher { Succeeded = true };
        var service = CreateService(auth, dispatcher);
        Assert.True(await service.RunEmberFunction("Windows_OpenFolder", ["test-folder"]));

        auth.SignIn(null);
        Assert.False(await service.RunEmberFunction("Windows_OpenFolder", ["test-folder"]));
        Assert.Single(dispatcher.Users);
    }

    [Fact]
    public async Task HelperFailureCannotBeReportedAsSuccessful()
    {
        var auth = new TestAuthenticationStateProvider("user");
        var dispatcher = new RecordingDispatcher { Succeeded = false };
        var service = CreateService(auth, dispatcher);

        Assert.False(await service.RunEmberFunction("OutlookEmail_CreateNew", ["recipient@example.test", "Subject", "Body"]));
        Assert.Single(dispatcher.Users);
    }

    [Fact]
    public async Task DispatcherExceptionDoesNotCrashTheCircuitOrReportSuccess()
    {
        var auth = new TestAuthenticationStateProvider("user");
        var dispatcher = new RecordingDispatcher { Throw = true };
        var service = CreateService(auth, dispatcher);

        Assert.False(await service.RunEmberFunction("OutlookEmail_CreateNew", ["recipient@example.test", "Subject", "Body"]));
    }

    [Fact]
    public async Task BackgroundNotificationFailureCanRemainQuiet()
    {
        var toast = DispatchProxy.Create<IToastService, ToastProxy>();
        var service = new EmberService(new TestAuthenticationStateProvider("user"),
            new RecordingDispatcher(), toast, NullLogger<EmberService>.Instance);

        Assert.False(await service.RunEmberFunction("ShowTrayNotification", ["Incoming call"], showFailure: false));
        Assert.Equal(0, ((ToastProxy)(object)toast).Calls);
    }

    private static EmberService CreateService(AuthenticationStateProvider auth, IBridgeDispatcher dispatcher) =>
        new(auth, dispatcher, DispatchProxy.Create<IToastService, ToastProxy>(), NullLogger<EmberService>.Instance);

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal user = new(new ClaimsIdentity());
        public int ReadCount { get; private set; }
        public TestAuthenticationStateProvider(string? userId = null) => SignIn(userId);
        public void SignIn(string? userId) => user = userId == null ? new(new ClaimsIdentity()) :
            new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ReadCount++;
            return Task.FromResult(new AuthenticationState(user));
        }
    }

    private sealed class RecordingDispatcher : IBridgeDispatcher
    {
        public List<string?> Users { get; } = [];
        public bool Succeeded { get; init; }
        public bool Throw { get; init; }
        public Task<BridgeResponse> DispatchAsync(ClaimsPrincipal user, string command, List<string> parameters,
            CancellationToken cancellationToken = default)
        {
            Users.Add(user.FindFirstValue(ClaimTypes.NameIdentifier));
            if (Throw)
                throw new InvalidOperationException("Synthetic dispatcher failure");
            return Task.FromResult(new BridgeResponse { Succeeded = Succeeded, Error = Succeeded ? string.Empty : "Helper unavailable" });
        }
    }

    public class ToastProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            var returnType = targetMethod!.ReturnType;
            if (returnType == typeof(void)) return null;
            if (returnType == typeof(Task)) return Task.CompletedTask;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
