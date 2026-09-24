using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Quickfire.Shared.Bridge;
using Quickfire.Blazor.Domain.Ember;

namespace Quickfire.Blazor.Infrastructure.Bridge;

public interface IBridgeDispatcher
{
    Task<BridgeResponse> DispatchAsync(ClaimsPrincipal user, string command, List<string> parameters,
        CancellationToken cancellationToken = default);
}

public sealed class BridgeDispatcher(
    BridgeDevices devices,
    BridgeConnections connections,
    IHubContext<EmberHub> hub,
    TimeProvider time,
    ILogger<BridgeDispatcher> logger) : IBridgeDispatcher
{
    private sealed record Pending(Guid DeviceId, string UserId, string ConnectionId,
        TaskCompletionSource<BridgeResponse> Completion);
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _admission = new(1, 1);

    public async Task<BridgeResponse> DispatchAsync(ClaimsPrincipal principal, string command, List<string> parameters,
        CancellationToken cancellationToken = default)
    {
        var user = await devices.RequireUserAsync(principal, cancellationToken);
        if (!BridgeCommands.IsAllowed(command))
            return Failure("This helper command is not supported.");
        if (!ValidStrings(parameters, 256, 1024 * 1024))
            return Failure("The helper request is too large or has invalid parameters.");
        var selected = await devices.GetSelectedAsync(user.Id, cancellationToken);
        var connection = selected == null ? null : connections.Find(selected.Id);
        if (connection == null)
            return Failure("Your selected Office helper is offline or unpaired. Connect it from your profile.");
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new Pending(selected!.Id, user.Id, connection.ConnectionId,
            new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        await _admission.WaitAsync(cancellationToken);
        try
        {
            if (_pending.Values.Count(x => x.UserId == user.Id) >= 16)
                return Failure("Too many helper requests are pending. Wait for the current action to finish.");
            _pending.TryAdd(requestId, pending);
        }
        finally { _admission.Release(); }
        var expiresUtc = time.GetUtcNow().AddSeconds(60);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60), time);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            // Revalidate immediately before dispatch; no fallback to another device or user group.
            selected = await devices.ValidateAsync(selected.Id, user.Id, requestCancellation.Token);
            if (selected?.IsSelected != true || connections.Find(selected.Id)?.ConnectionId != connection.ConnectionId)
                return Failure("The selected helper changed or disconnected. Try again.", requestId);
            await hub.Clients.Client(connection.ConnectionId).SendAsync("ReceiveEmberCommand", new BridgeCommand
            {
                RequestId = requestId, ExpiresUtc = expiresUtc,
                Command = command, Parameters = new List<string>(parameters)
            }, requestCancellation.Token);
            logger.LogInformation("Dispatched helper command {Command} with request {RequestId} to device {DeviceId}", command, requestId, selected.Id);
            return await pending.Completion.Task.WaitAsync(requestCancellation.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Failure("The helper did not confirm this action. Check the helper before retrying.", requestId);
        }
        finally { _pending.TryRemove(requestId, out _); }
    }

    public async Task CompleteAsync(BridgeDevice device, string connectionId, BridgeResponse response)
    {
        device = await devices.ValidateAsync(device.Id, device.UserId)
            ?? throw new HubException("This helper is no longer paired.");
        if (!device.IsSelected || device.Scope != BridgeScopes.Office || connections.Find(device.Id)?.ConnectionId != connectionId)
            throw new HubException("This helper is no longer the selected Office connection.");
        if (response == null || string.IsNullOrWhiteSpace(response.RequestId) || response.RequestId.Length != 32
            || response.Error?.Length > 2048 || !ValidStrings(response.Data, 256, 4 * 1024 * 1024))
            throw new HubException("Invalid helper response.");
        if (!_pending.TryGetValue(response.RequestId, out var pending) || pending.DeviceId != device.Id
            || pending.UserId != device.UserId || pending.ConnectionId != connectionId)
            throw new HubException("This helper response does not match a pending request.");
        if (!_pending.TryRemove(response.RequestId, out _))
            throw new HubException("This helper request has already completed.");
        pending.Completion.TrySetResult(new BridgeResponse
        {
            RequestId = response.RequestId, Succeeded = response.Succeeded,
            Error = response.Error ?? string.Empty, Data = new List<string>(response.Data)
        });
    }

    public void Disconnect(Guid deviceId, string connectionId)
    {
        foreach (var entry in _pending.Where(x => x.Value.DeviceId == deviceId && x.Value.ConnectionId == connectionId))
            if (_pending.TryRemove(entry.Key, out var pending))
                pending.Completion.TrySetResult(Failure("The helper disconnected before confirming this action.", entry.Key));
    }

    private static bool ValidStrings(List<string>? values, int maxCount, int maxCharacters)
    {
        if (values == null || values.Count > maxCount)
            return false;
        long total = 0;
        foreach (var value in values)
        {
            if (value == null || (total += value.Length) > maxCharacters)
                return false;
        }
        return true;
    }

    private static BridgeResponse Failure(string error, string requestId = "") => new() { RequestId = requestId, Error = error };
}
