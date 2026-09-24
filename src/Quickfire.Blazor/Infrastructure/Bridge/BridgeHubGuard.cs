using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Quickfire.Blazor.Domain.Ember;

namespace Quickfire.Blazor.Infrastructure.Bridge;

public sealed class BridgeHubGuard(BridgeDevices devices) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var expectedScope = invocationContext.Hub is EmberHub ? BridgeScopes.Office : BridgeScopes.IncomingCall;
        await RequireDeviceAsync(invocationContext.Context, expectedScope);
        return await next(invocationContext);
    }

    public async Task<BridgeDevice> RequireDeviceAsync(HubCallerContext context, string scope)
    {
        var id = context.User?.FindFirstValue(BridgeAuthentication.DeviceClaim);
        var owner = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context.User?.Identity?.IsAuthenticated == true && Guid.TryParseExact(id, "N", out var deviceId))
        {
            var device = await devices.ValidateAsync(deviceId, owner, context.ConnectionAborted);
            if (device != null && device.Scope == scope)
                return device;
        }
        context.Abort();
        throw new HubException("Helper credential is unavailable. Connect this helper again from your profile.");
    }
}
