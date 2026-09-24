using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using Quickfire.Blazor.Data;

namespace Quickfire.Blazor.Infrastructure.Bridge;

public sealed class BridgeNotifications(BridgeDevices devices, ILogger<BridgeNotifications> logger, TimeProvider? clock = null) : IDisposable
{
    private sealed record Delivery(Guid PublisherId, string UserId, string Number, string Name, string? SessionId, DateTimeOffset Expires);
    private sealed class Subscription(ClaimsPrincipal principal, Func<CallInfo, Task> handler)
    {
        public ClaimsPrincipal Principal { get; } = principal;
        public Func<CallInfo, Task> Handler { get; } = handler;
        public CancellationTokenSource Cancellation { get; } = new();
        public Channel<Delivery> Queue { get; } = Channel.CreateBounded<Delivery>(new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false
        });
        private int _stopped;
        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            Cancellation.Cancel();
            Queue.Writer.TryComplete();
            while (Queue.Reader.TryRead(out _)) { }
        }
    }
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    private readonly ConcurrentDictionary<Guid, Task> _workers = new();
    private readonly TimeProvider _time = clock ?? TimeProvider.System;
    private readonly object _lifecycle = new();
    private bool _disposed;

    public IDisposable Subscribe(ClaimsPrincipal user, Func<CallInfo, Task> handler)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(user.FindFirstValue(ClaimTypes.NameIdentifier)))
            throw new UnauthorizedAccessException("Sign in to receive call notifications.");
        ArgumentNullException.ThrowIfNull(handler);
        var id = Guid.NewGuid();
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var subscription = new Subscription(user.Clone(), handler);
            _subscriptions[id] = subscription;
            _workers[id] = Task.Run(() => ConsumeAsync(id, subscription)); // Never bind the worker to a Blazor circuit.
        }
        return new Unsubscribe(() => Remove(id));
    }

    public async Task PublishAsync(BridgeDevice publisher, CallInfo call)
    {
        if (publisher.Scope != BridgeScopes.IncomingCall || call == null || string.IsNullOrWhiteSpace(call.CallerId)
            || call.CallerId.Length > 128 || call.CallerName?.Length > 256 || call.SessionId?.Length > 128)
            throw new ArgumentException("Invalid incoming-call notification.");
        var currentPublisher = await devices.ValidateAsync(publisher.Id, publisher.UserId);
        if (currentPublisher?.Scope != BridgeScopes.IncomingCall)
            throw new UnauthorizedAccessException("This incoming-call helper is no longer paired.");
        var delivery = new Delivery(publisher.Id, publisher.UserId, call.CallerId, call.CallerName ?? string.Empty,
            call.SessionId, _time.GetUtcNow().AddSeconds(30));
        foreach (var entry in _subscriptions.Where(x => x.Value.Principal.FindFirstValue(ClaimTypes.NameIdentifier) == publisher.UserId))
        {
            // A slow browser must not hold the sender's acknowledgement or another browser's delivery.
            if (!entry.Value.Queue.Writer.TryWrite(delivery))
                logger.LogWarning("Skipped a browser call event because its bounded delivery queue is unavailable.");
        }
    }

    private async Task ConsumeAsync(Guid id, Subscription subscription)
    {
        var cancellationToken = subscription.Cancellation.Token;
        try
        {
            await foreach (var delivery in subscription.Queue.Reader.ReadAllAsync(cancellationToken))
            {
                if (delivery.Expires <= _time.GetUtcNow()) continue;
                await DeliverAsync(id, subscription, delivery, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { logger.LogWarning("A browser call subscription stopped unexpectedly."); }
        finally { Remove(id); subscription.Cancellation.Dispose(); _workers.TryRemove(id, out _); }
    }

    private async Task DeliverAsync(Guid id, Subscription subscription, Delivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            var publisher = await devices.ValidateAsync(delivery.PublisherId, delivery.UserId, cancellationToken);
            if (publisher?.Scope != BridgeScopes.IncomingCall) return;
            await devices.RequireUserAsync(subscription.Principal, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (delivery.Expires <= _time.GetUtcNow()) return;
            // Only the presentation callback can outlive cancellation. Database scopes have already closed.
            var callback = subscription.Handler(new CallInfo { CallerId = delivery.Number, CallerName = delivery.Name, SessionId = delivery.SessionId });
            await ObserveCallbackAsync(callback).WaitAsync(cancellationToken);
        }
        catch (UnauthorizedAccessException) { Remove(id); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) { logger.LogWarning("A call-notification subscriber could not receive an event."); }
    }

    private async Task ObserveCallbackAsync(Task callback)
    {
        try { await callback; }
        catch (Exception) { logger.LogWarning("A browser could not present a call event."); }
    }

    private void Remove(Guid id)
    {
        if (_subscriptions.TryRemove(id, out var subscription)) subscription.Stop();
    }

    public void Dispose()
    {
        Task[] workers;
        lock (_lifecycle)
        {
            _disposed = true;
            foreach (var id in _subscriptions.Keys) Remove(id);
            workers = _workers.Values.ToArray();
        }
        Task.WhenAll(workers).GetAwaiter().GetResult();
    }

    private sealed class Unsubscribe(Action action) : IDisposable
    {
        private Action? _action = action;
        public void Dispose() => Interlocked.Exchange(ref _action, null)?.Invoke();
    }
}
