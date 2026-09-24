using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Quickfire.Blazor.Data;
using Quickfire.Blazor.Infrastructure.Bridge;

[Authorize(AuthenticationSchemes = BridgeAuthentication.SchemeName, Policy = BridgeAuthentication.CallPolicy)]
public sealed class NotificationHub(BridgeHubGuard guard, BridgeConnections connections,
    BridgeNotifications notifications, BridgeCallToasts toasts) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var device = await guard.RequireDeviceAsync(Context, BridgeScopes.IncomingCall);
        connections.Register(new BridgeConnection(device.Id, device.UserId, Context.ConnectionId, Context.Abort));
        await base.OnConnectedAsync();
    }

    public async Task SendIncomingCall(CallInfo callInfo)
    {
        var device = await guard.RequireDeviceAsync(Context, BridgeScopes.IncomingCall);
        try { await notifications.PublishAsync(device, callInfo); }
        catch (ArgumentException) { throw new HubException("Invalid incoming-call notification."); }
        // Fixed server-created command, once per accepted call, independent of browser tabs.
        toasts.Enqueue(Context.User!, callInfo);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Guid.TryParseExact(Context.User?.FindFirstValue(BridgeAuthentication.DeviceClaim), "N", out var deviceId))
            connections.Remove(deviceId, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
