using System.Security.Claims;
using System.Threading.Channels;
using Quickfire.Blazor.Data;

namespace Quickfire.Blazor.Infrastructure.Bridge;

// Browser delivery is authoritative. Optional desktop toasts must not delay acknowledgement
// to a phone integration, whose retry could otherwise produce a duplicate call notification.
public sealed class BridgeCallToasts(IBridgeDispatcher dispatcher, TimeProvider time, ILogger<BridgeCallToasts> logger) : BackgroundService
{
    private sealed record Toast(ClaimsPrincipal Principal, string Number, string Name, DateTimeOffset Expires);
    private readonly Channel<Toast> _queue = Channel.CreateBounded<Toast>(new BoundedChannelOptions(16)
    {
        FullMode = BoundedChannelFullMode.Wait, SingleWriter = false, SingleReader = false
    });

    public void Enqueue(ClaimsPrincipal principal, CallInfo call)
    {
        if (call.CallerId.Count(char.IsDigit) < 7)
            return;
        if (!_queue.Writer.TryWrite(new Toast(principal.Clone(), call.CallerId, call.CallerName ?? string.Empty, time.GetUtcNow().AddSeconds(10))))
            logger.LogWarning("Skipped an optional native call toast because the delivery queue is full.");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        Enumerable.Range(0, 4).Select(_ => DeliverAsync(stoppingToken)));

    private async Task DeliverAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var toast in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                var remaining = toast.Expires - time.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                    continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(remaining);
                try
                {
                    await dispatcher.DispatchAsync(toast.Principal, "ShowTrayNotification",
                        ["Incoming Call", $"{toast.Name} ({toast.Number})"], timeout.Token);
                }
                catch (OperationCanceledException) { /* Optional toast timed out or host is stopping; never retry. */ }
                catch (Exception) { logger.LogWarning("An optional native call toast could not be delivered; it was not retried."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
