using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Quickfire.Shared.Bridge;

namespace Quickfire.Tray
{
    public partial class SystemTray : Form
    {
        private readonly NativeCredentialStore _store;
        private readonly BridgeRequestTracker _requests = new BridgeRequestTracker();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _commandGate = new SemaphoreSlim(1, 1);
        private HubConnection _connection;
        private NativeExecutionSession _execution;
        private Uri _origin;
        private Uri _notificationUri;
        private readonly ToolStripMenuItem _status = new ToolStripMenuItem("Not paired") { Enabled = false };

        public SystemTray(string credentialDirectory = null)
        {
            _store = new NativeCredentialStore("tray", credentialDirectory);
            InitializeComponent();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            SurefireEmberIcon.Icon = SystemIcons.Application;
            SurefireEmberIcon.Text = "Quickfire helper - not paired";
            SurefireEmberIcon.BalloonTipClicked += (_, __) =>
            {
                if (_notificationUri == null || !NativeBridgeSafety.SameOrigin(_origin, _notificationUri)) return;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_notificationUri.AbsoluteUri) { UseShellExecute = true }); }
                catch { SetStatus("Unable to open the notification in a browser"); }
            };
            EmberContextMenu.Items.Clear();
            EmberContextMenu.Items.Add(_status);
            EmberContextMenu.Items.Add("Pair with Quickfire…", null, async (_, __) => await PairAsync());
            EmberContextMenu.Items.Add("Reconnect", null, async (_, __) => await ConnectAsync());
            EmberContextMenu.Items.Add("Forget pairing", null, async (_, __) => { await DisconnectAsync(); _store.Forget(); SetStatus("Not paired"); });
            EmberContextMenu.Items.Add(new ToolStripSeparator());
            EmberContextMenu.Items.Add("Start at Windows sign-in", null, (_, __) => AutoStartHelper.AddToStartup());
            EmberContextMenu.Items.Add("Stop starting at sign-in", null, (_, __) => AutoStartHelper.RemoveFromStartup());
            EmberContextMenu.Items.Add("Exit", null, (_, __) => Close());
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Hide();
            await ConnectAsync();
        }

        private async Task PairAsync()
        {
            using (var dialog = new Form { Text = "Pair Quickfire helper", Width = 520, Height = 270, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false })
            using (var server = new TextBox { Dock = DockStyle.Fill })
            using (var token = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 6, ColumnCount = 1 };
                layout.Controls.Add(new Label { Text = "Server origin (HTTPS, or HTTP loopback)", AutoSize = true });
                layout.Controls.Add(server);
                layout.Controls.Add(new Label { Text = "Device credential from your signed-in Quickfire profile", AutoSize = true });
                layout.Controls.Add(token);
                var error = new Label { AutoSize = true, ForeColor = Color.DarkRed };
                layout.Controls.Add(error);
                var save = new Button { Text = "Save pairing", AutoSize = true };
                layout.Controls.Add(save);
                save.Click += (_, __) =>
                {
                    try { _store.Save(server.Text.Trim(), token.Text.Trim()); dialog.DialogResult = DialogResult.OK; }
                    catch { error.Text = "Check the HTTPS server origin and device credential."; }
                };
                dialog.Controls.Add(layout);
                dialog.AcceptButton = save;
                try { var existing = _store.Load(); if (existing != null) server.Text = existing.Origin.AbsoluteUri; } catch { }
                if (dialog.ShowDialog() != DialogResult.OK) return;
            }
            await DisconnectAsync();
            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            await _connectGate.WaitAsync();
            try
            {
                if (_connection != null && _connection.State != HubConnectionState.Disconnected) return;
                if (_connection != null) { _execution?.Dispose(); await _connection.DisposeAsync(); }
                var credential = _store.Load();
                if (credential == null) { SetStatus("Not paired - use Pair with Quickfire"); return; }
                _origin = credential.Origin;
                var connection = new HubConnectionBuilder()
                    .WithUrl(new Uri(credential.Origin, "emberHub"), options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult(credential.Token);
                        options.Transports = HttpTransportType.LongPolling;
                        options.HttpMessageHandlerFactory = _ => new OriginBoundHandler(credential.Origin);
                    })
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                    .Build();
                _connection = connection;
                var execution = new NativeExecutionSession();
                _execution = execution;
                connection.On<BridgeCommand>("ReceiveEmberCommand", command => HandleCommandAsync(connection, execution, command));
                connection.Reconnecting += _ => { execution.Disconnect(); SetStatus("Reconnecting"); return Task.CompletedTask; };
                connection.Reconnected += _ => { execution.Reconnect(); SetStatus("Connected"); return Task.CompletedTask; };
                connection.Closed += _ => { execution.Disconnect(); SetStatus("Disconnected - check pairing"); return Task.CompletedTask; };
                SetStatus("Connecting");
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    await connection.StartAsync(timeout.Token);
                SetStatus("Connected");
            }
            catch { SetStatus("Unavailable - check server and pairing"); }
            finally { _connectGate.Release(); }
        }

        private async Task HandleCommandAsync(HubConnection connection, NativeExecutionSession execution, BridgeCommand command)
        {
            if (command == null || !_requests.TryBegin(command.RequestId)) return;
            var response = new BridgeResponse { RequestId = command.RequestId, Data = new List<string>() };
            var token = execution.Token;
            CancellationTokenSource deadline = null;
            var entered = false;
            try
            {
                NativeBridgeSafety.ValidateCommand(command);
                deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(command.ExpiresUtc - DateTimeOffset.UtcNow);
                token = deadline.Token;
                entered = await _commandGate.WaitAsync(0, token);
                if (!entered) { response.Error = "The helper is busy. The command was not queued."; return; }
                response = await OnUiAsync(() =>
                {
                    execution.EnsureExecutable(command, token);
                    if (!ReferenceEquals(connection, _connection) || connection.State != HubConnectionState.Connected)
                        throw new OperationCanceledException();
                    return Execute(command);
                }, token);
            }
            catch { response.Error = "The helper could not complete this command. Check the requested document or native application."; }
            finally
            {
                if (entered) _commandGate.Release();
                deadline?.Dispose();
                // One attempt only. A disconnect never queues or replays an action or its result.
                try { await connection.InvokeAsync("CompleteCommand", response); }
                catch { SetStatus("Response unavailable - command was not replayed"); }
            }
        }

        private BridgeResponse Execute(BridgeCommand command)
        {
            var response = new BridgeResponse { RequestId = command.RequestId, Succeeded = true, Data = new List<string>() };
            switch (command.Command)
            {
                case "GetWordDocContents":
                    WordControl.PerformWordFunction(command, completed => response = completed);
                    break;
                case "OutlookSearch_EmailStrictToFrom": case "OutlookSearch_EmailBroad": case "OutlookSearch_Policy":
                case "OutlookSearch_SmartSearch": case "OutlookSearch_Carrier": case "OutlookEmail_CreateNew":
                    OutlookControl.PerformOutlookFunction(command.Command, command.Parameters); break;
                case "Windows_OpenFolder": case "Windows_OpenFile":
                    WindowsControl.PerformWindowsFunction(command.Command, command.Parameters); break;
                case "ShowTrayNotification":
                    Require(command.Parameters, 2); Notify(command.Parameters[0], command.Parameters[1], ClientLink(command.Parameters)); break;
                case "ShowStaffChat":
                    Require(command.Parameters, 3); Notify("Message from " + command.Parameters[2], command.Parameters[1], _origin); break;
                case "Windows_ShowCallNotification":
                    Require(command.Parameters, 2); Notify("Incoming call", command.Parameters[0] + " " + command.Parameters[1], ClientLink(command.Parameters)); break;
                case "update_available": case "update_prompt":
                    Notify("Quickfire update", command.Parameters.Count > 0 ? command.Parameters[0] : "An update is available."); break;
                default: throw new NotSupportedException();
            }
            return response;
        }

        private static void Require(List<string> parameters, int count) { if (parameters.Count < count) throw new ArgumentException(); }
        private Uri ClientLink(List<string> parameters)
        {
            long clientId;
            return _origin != null && parameters.Count > 2 && long.TryParse(parameters[2], out clientId) && clientId > 0
                ? new Uri(_origin, "Clients/" + clientId.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
        }
        private void Notify(string title, string text, Uri link = null)
        {
            _notificationUri = link;
            SurefireEmberIcon.BalloonTipTitle = title.Length > 63 ? title.Substring(0, 63) : title;
            SurefireEmberIcon.BalloonTipText = text.Length > 255 ? text.Substring(0, 255) : text;
            SurefireEmberIcon.ShowBalloonTip(5000);
        }
        private async Task<BridgeResponse> OnUiAsync(Func<BridgeResponse> action, CancellationToken token)
        {
            using (var work = new NativeQueuedWork<BridgeResponse>(action, token))
            {
                BeginInvoke((Action)work.Execute);
                return await work.Completion;
            }
        }
        private void SetStatus(string text)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => SetStatus(text))); return; }
            _status.Text = text;
            SurefireEmberIcon.Text = "Quickfire helper - " + (text.Length > 44 ? text.Substring(0, 44) : text);
        }
        private async Task DisconnectAsync()
        {
            await _connectGate.WaitAsync();
            try { _execution?.Dispose(); _notificationUri = null; var previous = _connection; _connection = null; if (previous != null) await previous.DisposeAsync(); }
            finally { _connectGate.Release(); }
        }
        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            SurefireEmberIcon.Visible = false;
            await DisconnectAsync();
            base.OnFormClosed(e);
        }
    }
}
