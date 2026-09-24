using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Quickfire.Shared.Bridge;
using Quickfire.Blazor.Infrastructure.Bridge;

namespace Quickfire.Blazor.Domain.Ember;

[Authorize(AuthenticationSchemes = BridgeAuthentication.SchemeName, Policy = BridgeAuthentication.OfficePolicy)]
public sealed class EmberHub(BridgeHubGuard guard, BridgeConnections connections, BridgeDispatcher dispatcher) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var device = await guard.RequireDeviceAsync(Context, BridgeScopes.Office);
        connections.Register(new BridgeConnection(device.Id, device.UserId, Context.ConnectionId, Context.Abort));
        await base.OnConnectedAsync();
    }

    public async Task CompleteCommand(BridgeResponse response)
    {
        var device = await guard.RequireDeviceAsync(Context, BridgeScopes.Office);
        await dispatcher.CompleteAsync(device, Context.ConnectionId, response);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Guid.TryParseExact(Context.User?.FindFirstValue(BridgeAuthentication.DeviceClaim), "N", out var deviceId))
        {
            connections.Remove(deviceId, Context.ConnectionId);
            dispatcher.Disconnect(deviceId, Context.ConnectionId);
        }
        return base.OnDisconnectedAsync(exception);
    }
}
