using Microsoft.Maui.Controls;
using Quickfire.Shared.Bridge;
using Quickfire.Desktop.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quickfire.Desktop
{
    public partial class MainPage : ContentPage
    {
        private readonly IQuickfireHost _quickfireHost;
        private readonly IDesktopEmberBridge _emberBridge;

        private CancellationTokenSource? _startupCts;
        private bool _hasInitialized;
        private Uri? _hostBaseUri;
        private Uri? _managedHostOrigin;
        private bool _changingServer;
        // Preserve the saved server selection across the project rename.
        private const string ServerPreference = "Openfire.ServerOrigin";

        public MainPage(IQuickfireHost quickfireHost, IDesktopEmberBridge emberBridge)
        {
            InitializeComponent();
            _quickfireHost = quickfireHost;
            _emberBridge = emberBridge;
            _emberBridge.ConnectionStateChanged += connected => MainThread.BeginInvokeOnMainThread(() =>
                BridgeStatus.Text = connected ? "Helper connected" : "Helper disconnected");
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (_hasInitialized)
            {
                return;
            }

            _hasInitialized = true;
            _ = StartQuickfireAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _startupCts?.Cancel();
        }

        private void OnRetryClicked(object sender, EventArgs e)
        {
            _ = StartQuickfireAsync();
        }

        private async Task StartQuickfireAsync()
        {
            SetLoadingState("Starting Quickfire...", showRetry: false);

            _startupCts?.Cancel();
            var cts = new CancellationTokenSource();
            _startupCts = cts;

            try
            {
                var selectedServer = Preferences.Default.Get(ServerPreference, "");
                var baseUri = await DesktopServerSelection.ResolveAsync(selectedServer,
                    _quickfireHost.EnsureStartedAsync, cts.Token).ConfigureAwait(false);
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (string.IsNullOrWhiteSpace(selectedServer)) _managedHostOrigin = baseUri;
                    _hostBaseUri = baseUri;
                    AppWebView.Source = baseUri.ToString();
                    ServerAddress.Text = baseUri.GetLeftPart(UriPartial.Authority);
                    AppWebView.IsVisible = true;
                    LoadingOverlay.IsVisible = false;
                    RetryButton.IsVisible = false;
                });
                // The web window remains usable when the optional native helper is unpaired or offline.
                _ = ConnectHelperAsync(baseUri, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception ex)
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                SetLoadingState($"Failed to start Quickfire: {ex.Message}", showRetry: true);
            }
        }

        private async Task ConnectHelperAsync(Uri baseUri, CancellationToken cancellationToken)
        {
            string status;
            try { status = await _emberBridge.EnsureConnectedAsync(baseUri, cancellationToken) ? "Helper connected" : "Helper not paired"; }
            catch { status = "Helper unavailable - check pairing"; }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && _hostBaseUri is not null
                    && NativeBridgeSafety.SameOrigin(baseUri, _hostBaseUri)) BridgeStatus.Text = status;
            });
        }

        private void OnPairHelperClicked(object sender, EventArgs e)
        {
            PairingServer.Text = _hostBaseUri?.GetLeftPart(UriPartial.Authority) ?? "";
            PairingToken.Text = "";
            PairingError.Text = "";
            PairingOverlay.IsVisible = true;
        }

        private async void OnSavePairingClicked(object sender, EventArgs e)
        {
            if (_changingServer) return;
            _changingServer = true;
            try
            {
                var origin = NativeBridgeSafety.ServerOrigin(PairingServer.Text?.Trim() ?? "");
                var credential = PairingToken.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(credential))
                {
                    PairingError.Text = "Open the server and create an automation device credential in your profile first.";
                    return;
                }
                await OpenServerAsync(origin);
                await _emberBridge.PairAsync(origin, origin.AbsoluteUri, credential);
                PairingToken.Text = "";
                PairingOverlay.IsVisible = false;
                BridgeStatus.Text = "Helper connected";
            }
            catch { PairingError.Text = "Use an HTTPS server origin (HTTP is allowed only on this computer) and a valid automation credential. Check the server certificate and connection."; }
            finally { _changingServer = false; }
        }

        private async void OnOpenServerClicked(object sender, EventArgs e)
        {
            if (_changingServer) return;
            _changingServer = true;
            try
            {
                var origin = NativeBridgeSafety.ServerOrigin(PairingServer.Text?.Trim() ?? "");
                await OpenServerAsync(origin);
                PairingToken.Text = "";
                PairingOverlay.IsVisible = false;
                _ = ConnectHelperAsync(origin, CancellationToken.None);
            }
            catch { PairingError.Text = "Enter an HTTPS server origin, or a loopback HTTP origin for a server on this computer."; }
            finally { _changingServer = false; }
        }

        private async Task OpenServerAsync(Uri origin)
        {
            _startupCts?.Cancel();
            await _emberBridge.DisposeAsync();
            var selectedServer = DesktopServerSelection.PreferenceFor(origin, _managedHostOrigin!);
            if (string.IsNullOrEmpty(selectedServer)) Preferences.Default.Remove(ServerPreference);
            else Preferences.Default.Set(ServerPreference, selectedServer);
            _hostBaseUri = origin;
            AppWebView.Source = origin.AbsoluteUri;
            ServerAddress.Text = origin.GetLeftPart(UriPartial.Authority);
            AppWebView.IsVisible = true;
            LoadingOverlay.IsVisible = false;
            RetryButton.IsVisible = false;
            BridgeStatus.Text = "Helper not paired";
        }

        private async void OnUseLocalServerClicked(object sender, EventArgs e)
        {
            if (_changingServer) return;
            _changingServer = true;
            _startupCts?.Cancel();
            try
            {
                await _emberBridge.DisposeAsync();
                Preferences.Default.Remove(ServerPreference);
                PairingToken.Text = "";
                PairingOverlay.IsVisible = false;
                await StartQuickfireAsync();
            }
            catch { BridgeStatus.Text = "Unable to switch servers. Try again."; }
            finally { _changingServer = false; }
        }

        private void OnCancelPairingClicked(object sender, EventArgs e)
        {
            PairingToken.Text = "";
            PairingOverlay.IsVisible = false;
        }

        private async void OnForgetHelperClicked(object sender, EventArgs e)
        {
            try { await _emberBridge.ForgetPairingAsync(); BridgeStatus.Text = "Helper not paired"; }
            catch { BridgeStatus.Text = "Unable to clear pairing"; }
        }

        private void SetLoadingState(string message, bool showRetry)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingMessage.Text = message;
                LoadingOverlay.IsVisible = true;
                RetryButton.IsVisible = showRetry;
                AppWebView.IsVisible = false;
            });
        }
    }
}
