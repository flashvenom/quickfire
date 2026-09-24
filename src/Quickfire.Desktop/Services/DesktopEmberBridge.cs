using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Quickfire.Shared.Bridge;
using Quickfire.Desktop.Interop;
using Quickfire.Tray;

namespace Quickfire.Desktop.Services;

public interface IDesktopEmberBridge : IAsyncDisposable
{
    event Action<bool>? ConnectionStateChanged;
    Task<bool> EnsureConnectedAsync(Uri hostBaseUri, CancellationToken cancellationToken = default);
    Task PairAsync(Uri hostBaseUri, string server, string token, CancellationToken cancellationToken = default);
    Task ForgetPairingAsync();
}

public sealed class DesktopEmberBridge : IDesktopEmberBridge
{
    public event Action<bool>? ConnectionStateChanged;
    private readonly ILogger<DesktopEmberBridge> _logger;
    private readonly NativeCredentialStore _store;
    private readonly BridgeRequestTracker _requests = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private HubConnection? _connection;
    private NativeExecutionSession? _execution;

    public DesktopEmberBridge(ILogger<DesktopEmberBridge> logger, string? credentialDirectory = null)
    {
        _logger = logger;
        _store = new NativeCredentialStore("desktop", credentialDirectory);
    }

    public async Task<bool> EnsureConnectedAsync(Uri hostBaseUri, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var credential = _store.Load();
            if (credential is null) return false;
            if (!NativeBridgeSafety.SameOrigin(credential.Origin, NativeBridgeSafety.ServerOrigin(hostBaseUri.ToString())))
                throw new InvalidOperationException("Pair the helper with the server displayed in this window.");
            if (_connection is not null && _connection.State != HubConnectionState.Disconnected) return true;
            if (_connection is not null) { _execution?.Dispose(); await _connection.DisposeAsync(); }
            var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(credential.Origin, "emberHub"), options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(credential.Token);
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => new OriginBoundHandler(credential.Origin);
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();
            _connection = connection;
            var execution = new NativeExecutionSession();
            _execution = execution;
            connection.On<BridgeCommand>("ReceiveEmberCommand", command => HandleCommandAsync(connection, execution, command));
            connection.Reconnecting += _ => { execution.Disconnect(); ConnectionStateChanged?.Invoke(false); return Task.CompletedTask; };
            connection.Reconnected += _ => { execution.Reconnect(); ConnectionStateChanged?.Invoke(true); return Task.CompletedTask; };
            connection.Closed += _ => { execution.Disconnect(); ConnectionStateChanged?.Invoke(false); _logger.LogInformation("Native helper disconnected. Check pairing before reconnecting."); return Task.CompletedTask; };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await connection.StartAsync(timeout.Token);
            ConnectionStateChanged?.Invoke(true);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task PairAsync(Uri hostBaseUri, string server, string token, CancellationToken cancellationToken = default)
    {
        if (!NativeBridgeSafety.SameOrigin(NativeBridgeSafety.ServerOrigin(server), NativeBridgeSafety.ServerOrigin(hostBaseUri.ToString())))
            throw new InvalidOperationException("Pair the helper with the server displayed in this window.");
        await DisposeAsync();
        _store.Save(server, token);
        await EnsureConnectedAsync(hostBaseUri, cancellationToken);
    }

    public async Task ForgetPairingAsync() { await DisposeAsync(); _store.Forget(); }

    private async Task HandleCommandAsync(HubConnection connection, NativeExecutionSession execution, BridgeCommand command)
    {
        if (command is null || !_requests.TryBegin(command.RequestId)) return;
        var response = new BridgeResponse { RequestId = command.RequestId };
        var entered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(execution.Token);
        var token = deadline.Token;
        void EnsureCurrent()
        {
            execution.EnsureExecutable(command, token);
            if (!ReferenceEquals(connection, _connection) || connection.State != HubConnectionState.Connected)
                throw new OperationCanceledException();
        }
        try
        {
            NativeBridgeSafety.ValidateCommand(command);
            deadline.CancelAfter(command.ExpiresUtc - DateTimeOffset.UtcNow);
            entered = await _commandGate.WaitAsync(0, token);
            if (!entered) { response.Error = "The helper is busy. The command was not queued."; return; }
            EnsureCurrent();
            switch (command.Command)
            {
                case "Windows_OpenFolder": case "Windows_OpenFile":
                    await RunOnStaThreadAsync(() => { EnsureCurrent(); WindowsControl.PerformWindowsFunction(command.Command, command.Parameters); }); break;
                case "OutlookSearch_EmailStrictToFrom": case "OutlookSearch_EmailBroad": case "OutlookSearch_Policy":
                case "OutlookSearch_SmartSearch": case "OutlookSearch_Carrier": case "OutlookEmail_CreateNew":
                    await RunOnStaThreadAsync(() => { EnsureCurrent(); OutlookControl.PerformOutlookFunction(command.Command, command.Parameters); }); break;
                case "GetWordDocContents":
                    response.Error = "Word document access requires the separately paired Windows tray helper.";
                    return;
                case "ShowTrayNotification":
                    await NotificationInterop.ShowTrayNotificationAsync(command.Parameters, _logger, token); break;
                case "ShowStaffChat":
                    await MessagingInterop.ShowStaffChatNotificationAsync(command.Parameters, _logger, token); break;
                case "Windows_ShowCallNotification":
                    if (command.Parameters.Count < 2) throw new ArgumentException();
                    await NotificationInterop.ShowTrayNotificationAsync(new[] { "Incoming call", command.Parameters[0] + " " + command.Parameters[1] }, _logger, token); break;
                case "update_available": case "update_prompt":
                    await UpdateInterop.HandleUpdateCommandAsync(command.Command, command.Parameters, _logger, token); break;
                default: throw new NotSupportedException();
            }
            response.Succeeded = true;
        }
        catch { response.Error = "The helper could not complete this command. Check the requested document or native application."; }
        finally
        {
            if (entered) _commandGate.Release();
            // No response queue: reconnecting must never replay a command or an uncertain completion.
            try { await connection.InvokeAsync("CompleteCommand", response); }
            catch { _logger.LogWarning("Native command response unavailable; the action was not replayed."); }
        }
    }

    private static Task RunOnStaThreadAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _execution?.Dispose(); var previous = _connection; _connection = null; if (previous is not null) await previous.DisposeAsync(); }
        finally { _gate.Release(); }
    }
}
