using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Storage;

namespace Quickfire.Desktop.Services
{
    public sealed class QuickfireHostService : IQuickfireHost
    {
        private readonly QuickfireHostOptions _options;
        private readonly ILogger<QuickfireHostService> _logger;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _workRoot;
        private readonly string _dataRoot;
        private readonly IDesktopSetupService _setupService;

        private string? _contentRoot;
        private Process? _process;
        private Uri? _baseAddress;
        private CancellationTokenSource? _processCts;

        public QuickfireHostService(
            IOptions<QuickfireHostOptions> options,
            ILogger<QuickfireHostService> logger,
            IDesktopSetupService setupService)
        {
            _options = options.Value ?? new QuickfireHostOptions();
            _logger = logger;
            _setupService = setupService;
            _httpClient = new HttpClient();

            var deploymentFolder = string.IsNullOrWhiteSpace(_options.DeploymentFolderName)
                ? "openfire-host"
                : _options.DeploymentFolderName.Trim();

            _workRoot = DesktopStorage.GetHostRoot(deploymentFolder);
            _dataRoot = DesktopStorage.GetDataRoot(_options.DeploymentFolderName, _options.DataDirectoryName);

            Directory.CreateDirectory(_workRoot);
            Directory.CreateDirectory(_dataRoot);
        }

        public async Task<Uri> EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Uri baseAddress;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EnsureFilesAsync(cancellationToken).ConfigureAwait(false);
                var setupState = await _setupService.GetStateAsync(cancellationToken).ConfigureAwait(false);
                await EnsureProcessAsync(setupState, cancellationToken).ConfigureAwait(false);
                baseAddress = _baseAddress ?? throw new InvalidOperationException("Quickfire base address was not initialized.");
            }
            finally
            {
                _gate.Release();
            }

            await WaitForReadyAsync(baseAddress, cancellationToken).ConfigureAwait(false);
            return baseAddress;
        }

        private async Task EnsureFilesAsync(CancellationToken cancellationToken)
        {
            var manifestLines = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            var manifestHash = ComputeHash(string.Join('\n', manifestLines.OrderBy(line => line, StringComparer.OrdinalIgnoreCase)));
            var hashPath = Path.Combine(_workRoot, ".manifest.hash");
            var contentFolder = Path.Combine(_workRoot, string.IsNullOrWhiteSpace(_options.ContentFolderName) ? "site" : _options.ContentFolderName.Trim());

            if (_contentRoot is not null &&
                Directory.Exists(_contentRoot) &&
                File.Exists(hashPath))
            {
                var existingHash = await File.ReadAllTextAsync(hashPath, cancellationToken).ConfigureAwait(false);
                if (string.Equals(existingHash, manifestHash, StringComparison.Ordinal))
                {
                    return;
                }
            }

            _logger.LogInformation("Extracting Quickfire desktop assets to {ContentRoot}", contentFolder);

            if (Directory.Exists(contentFolder))
            {
                Directory.Delete(contentFolder, recursive: true);
            }
            Directory.CreateDirectory(contentFolder);

            foreach (var entry in manifestLines)
            {
                var relativePath = entry;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                await CopyAssetAsync(relativePath, contentFolder, cancellationToken).ConfigureAwait(false);
            }

            EnsureSqliteShim(contentFolder);
            await File.WriteAllTextAsync(hashPath, manifestHash, cancellationToken).ConfigureAwait(false);
            _contentRoot = contentFolder;
        }

        private async Task EnsureProcessAsync(DesktopSetupState setupState, CancellationToken cancellationToken)
        {
            if (_process is { HasExited: false } && _baseAddress is not null)
            {
                return;
            }

            await StopProcessAsync().ConfigureAwait(false);

            if (_contentRoot is null)
            {
                throw new InvalidOperationException("Quickfire content root is not available.");
            }

            var port = _options.Port > 0 ? _options.Port : 5128;
            var uriBuilder = new UriBuilder(Uri.UriSchemeHttp, "127.0.0.1", port);
            _baseAddress = uriBuilder.Uri;

            var exeCandidates = OperatingSystem.IsWindows()
                ? new[]
                {
                    Path.Combine(_contentRoot, "Quickfire.Blazor.exe"),
                    Path.Combine(_contentRoot, "Quickfire.exe"),
                    Path.Combine(_contentRoot, "Surefire.Blazor.exe"),
                    Path.Combine(_contentRoot, "Surefire.exe")
                }
                : new[]
                {
                    Path.Combine(_contentRoot, "Quickfire"),
                    Path.Combine(_contentRoot, "Surefire")
                };
            var dllCandidates = new[]
            {
                Path.Combine(_contentRoot, "Quickfire.Blazor.dll"),
                Path.Combine(_contentRoot, "Surefire.Blazor.dll")
            };

            var exePath = exeCandidates.FirstOrDefault(File.Exists);
            var dllPath = dllCandidates.FirstOrDefault(File.Exists);

            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows() && exePath is not null)
            {
                startInfo = new ProcessStartInfo(exePath);
            }
            else if (dllPath is not null)
            {
                startInfo = new ProcessStartInfo("dotnet");
                startInfo.ArgumentList.Add(dllPath);
            }
            else
            {
                var probePath = exeCandidates.Concat(dllCandidates).FirstOrDefault() ?? "Quickfire.Blazor.exe";
                throw new FileNotFoundException("Quickfire executable could not be located in the packaged assets.", probePath);
            }

            startInfo.WorkingDirectory = _contentRoot;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;

            var environmentName = string.IsNullOrWhiteSpace(_options.EnvironmentName) ? "Desktop" : _options.EnvironmentName;
            startInfo.Environment["ASPNETCORE_URLS"] = _baseAddress.ToString();
            startInfo.Environment["DOTNET_ENVIRONMENT"] = environmentName;
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = environmentName;
            startInfo.Environment["QUICKFIRE_DESKTOP"] = "1";
            ApplySetupConfiguration(startInfo, setupState);

            _logger.LogInformation("Starting Quickfire host from {WorkingDirectory} on {Url}", _contentRoot, _baseAddress);

            _processCts = new CancellationTokenSource();
            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the Quickfire process.");
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) =>
            {
                if (_process?.ExitCode is int code)
                {
                    _logger.LogWarning("Quickfire process exited with code {ExitCode}", code);
                }
            };

            _ = Task.Run(() => PumpOutputAsync(_process.StandardOutput, LogLevel.Information, _processCts.Token));
            _ = Task.Run(() => PumpOutputAsync(_process.StandardError, LogLevel.Error, _processCts.Token));
        }

        private void ApplySetupConfiguration(ProcessStartInfo startInfo, DesktopSetupState setupState)
        {
            var dataDirectory = ResolveDataDirectory(setupState);
            startInfo.Environment["QUICKFIRE_DIR"] = dataDirectory;

            if (setupState.Database?.Mode == DesktopDatabaseMode.Remote)
            {
                startInfo.Environment["QUICKFIRE_DB"] = "SqlServer";
                var connection = setupState.Database.RemoteConnectionString;
                if (!string.IsNullOrWhiteSpace(connection))
                {
                    startInfo.Environment["DEFAULTCONNECTION"] = connection;
                }
            }
            else
            {
                startInfo.Environment["QUICKFIRE_DB"] = "Sqlite";
                var dbPath = setupState.Database?.LocalDatabasePath;
                if (!string.IsNullOrWhiteSpace(dbPath))
                {
                    startInfo.Environment["DEFAULTCONNECTION"] = $"Data Source={dbPath};Cache=Shared";
                }
            }

            if (setupState.Admin is { } admin)
            {
                if (!string.IsNullOrWhiteSpace(admin.Email))
                {
                    startInfo.Environment["ADMIN_EMAIL"] = admin.Email;
                    startInfo.Environment["ADMIN_USERNAME"] = admin.Email;
                }

                if (!string.IsNullOrWhiteSpace(admin.FirstName))
                {
                    startInfo.Environment["ADMIN_FIRSTNAME"] = admin.FirstName;
                }

                if (!string.IsNullOrWhiteSpace(admin.LastName))
                {
                    startInfo.Environment["ADMIN_LASTNAME"] = admin.LastName;
                }
            }

            if (!string.IsNullOrWhiteSpace(setupState.AdminPassword))
            {
                startInfo.Environment["ADMIN_PASSWORD"] = setupState.AdminPassword;
            }

            var pictureUrl = ResolveAdminPictureUrl(setupState.Admin?.PictureUrl);
            startInfo.Environment["ADMIN_PICTURE"] = pictureUrl;

        }

        private static string ResolveAdminPictureUrl(string? pictureUrl)
        {
            return string.IsNullOrWhiteSpace(pictureUrl) ? "default.jpg" : pictureUrl.Trim();
        }

        private string ResolveDataDirectory(DesktopSetupState setupState)
        {
            if (setupState.Database?.Mode == DesktopDatabaseMode.Local)
            {
                var path = setupState.Database.LocalDatabasePath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        return directory;
                    }
                }
            }

            Directory.CreateDirectory(_dataRoot);
            return _dataRoot;
        }

        private void EnsureSqliteShim(string contentFolder)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var nativeFolder = Path.Combine(contentFolder, "runtimes", "win-x64", "native");
                CopyIfMissing(Path.Combine(nativeFolder, "e_sqlite3.dll"), Path.Combine(contentFolder, "e_sqlite3.dll"), "SQLite native shim");
                CopyIfMissing(Path.Combine(nativeFolder, "vec0.dll"), Path.Combine(contentFolder, "vec0.dll"), "sqlite-vec extension");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy SQLite native dependencies.");
            }
        }

        private void CopyIfMissing(string source, string destination, string description)
        {
            if (File.Exists(source))
            {
                if (!File.Exists(destination))
                {
                    File.Copy(source, destination);
                    _logger.LogDebug("Copied {Source} to {Destination} for {Description}.", source, destination, description);
                }
            }
            else
            {
                _logger.LogWarning("{Description} missing at {Source}.", description, source);
            }
        }

        private async Task<IReadOnlyList<string>> LoadManifestAsync(CancellationToken cancellationToken)
        {
            var manifestAsset = BuildAssetPath(_options.ManifestFileName ?? "manifest.txt");
            await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(manifestAsset).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
            var content = await reader.ReadToEndAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var segments = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>(segments.Length);
            foreach (var segment in segments)
            {
                var trimmed = segment.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    lines.Add(trimmed);
                }
            }

            return lines;
        }

        private async Task CopyAssetAsync(string relativePath, string contentRoot, CancellationToken cancellationToken)
        {
            var normalizedRelative = relativePath.Replace('\\', '/');
            var asset = BuildAssetPath(normalizedRelative);

            await using var assetStream = await FileSystem.Current.OpenAppPackageFileAsync(asset).ConfigureAwait(false);
            var destinationPath = Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            await using var destinationStream = File.Create(destinationPath);
            await assetStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
        }

        private static string ComputeHash(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private string BuildAssetPath(string relativePath)
        {
            var prefix = string.IsNullOrWhiteSpace(_options.AssetRoot) ? "QuickfireHost" : _options.AssetRoot.TrimEnd('/', '\\');
            var sanitized = relativePath.TrimStart('/');
            return $"{prefix}/{sanitized}";
        }

        private async Task WaitForReadyAsync(Uri baseAddress, CancellationToken cancellationToken)
        {
            var readySegment = string.IsNullOrWhiteSpace(_options.ReadyPath) ? "_framework/blazor.server.js" : _options.ReadyPath.TrimStart('/');
            var readyUri = new Uri(baseAddress, readySegment);
            var timeoutSeconds = _options.StartupTimeoutSeconds > 0 ? _options.StartupTimeoutSeconds : 120;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var watch = Stopwatch.StartNew();

            while (watch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var response = await _httpClient.GetAsync(readyUri, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Quickfire host is ready at {Url}", baseAddress);
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // server not ready yet
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // treat as retry
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"Timed out waiting for Quickfire to start after {timeoutSeconds} seconds.");
        }

        private async Task PumpOutputAsync(StreamReader reader, LogLevel level, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _logger.Log(level, "[Quickfire] {Message}", line);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Ignore during shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Quickfire log pump stopped unexpectedly.");
            }
        }

        private async Task StopProcessAsync()
        {
            var proc = _process;
            _process = null;

            if (proc is null)
            {
                return;
            }

            try
            {
                if (!proc.HasExited)
                {
                    // Attempt soft shutdown first
                    bool softShutdownSucceeded = false;
                    
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            // Try to close the main window gracefully (sends WM_CLOSE)
                            if (proc.CloseMainWindow())
                            {
                                _logger.LogInformation("Sent close signal to Quickfire process, waiting for graceful shutdown...");
                                
                                // Wait up to 5 seconds for graceful shutdown
                                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                                try
                                {
                                    await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                                    softShutdownSucceeded = true;
                                    _logger.LogInformation("Quickfire process exited gracefully.");
                                }
                                catch (OperationCanceledException)
                                {
                                    _logger.LogWarning("Quickfire process did not exit within timeout, will force kill.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to close main window, will force kill.");
                        }
                    }
                    
                    // If soft shutdown didn't work, force kill
                    if (!softShutdownSucceeded && !proc.HasExited)
                    {
                        _logger.LogInformation("Force killing Quickfire process...");
                        proc.Kill(entireProcessTree: true);
                        await proc.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to stop Quickfire process cleanly.");
            }
            finally
            {
                proc.Dispose();
                _processCts?.Cancel();
                _processCts?.Dispose();
                _processCts = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            _gate.Dispose();
            await StopProcessAsync().ConfigureAwait(false);
        }
    }
}
