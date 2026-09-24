using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Quickfire.Shared.Bridge
{
    public static class NativeBridgeSafety
    {
        private static readonly HashSet<string> DocumentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".rtf",
            ".odt", ".ods", ".png", ".jpg", ".jpeg", ".gif", ".tif", ".tiff", ".bmp"
        };

        public static Uri ServerOrigin(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || uri.AbsolutePath != "/" || (uri.Scheme != "https" && uri.Scheme != "http"))
                throw new ArgumentException("Enter a server origin such as https://quickfire.example, without a path or credentials.");
            IPAddress address;
            var host = uri.DnsSafeHost.Trim('[', ']');
            var loopback = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(host, out address) && IPAddress.IsLoopback(address));
            if (uri.Scheme != "https" && !loopback)
                throw new ArgumentException("HTTPS is required except for a loopback server.");
            return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        }

        public static bool SameOrigin(Uri left, Uri right)
        {
            return left != null && right != null && left.Scheme == right.Scheme
                && string.Equals(left.DnsSafeHost, right.DnsSafeHost, StringComparison.OrdinalIgnoreCase)
                && left.Port == right.Port && string.IsNullOrEmpty(right.UserInfo);
        }

        public static void ValidateCommand(BridgeCommand command)
        {
            Guid request;
            if (command == null || !Guid.TryParse(command.RequestId, out request) || request == Guid.Empty)
                throw new ArgumentException("Invalid bridge request identifier.");
            if (!BridgeCommands.IsAllowed(command.Command))
                throw new ArgumentException("Unsupported bridge command.");
            ValidateDeadline(command);
            if (command.Parameters == null || command.Parameters.Count > 64
                || command.Parameters.Any(p => p == null || p.Length > 262144))
                throw new ArgumentException("Invalid bridge command parameters.");
            if (command.Command == "Windows_OpenFile" || command.Command == "Windows_OpenFolder")
            {
                if (command.Parameters.Count != 1) throw new ArgumentException("A single local path is required.");
                ValidatePath(command.Parameters[0], command.Command == "Windows_OpenFile");
            }
            if (command.Command == "GetWordDocContents" && command.Parameters.Count != 0)
                throw new ArgumentException("Word contents does not accept parameters.");
        }

        public static void ValidateDeadline(BridgeCommand command)
        {
            if (command == null || command.ExpiresUtc.Offset != TimeSpan.Zero || command.ExpiresUtc <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("This bridge request has expired.");
        }

        // Validate syntax independently of filesystem existence so it is testable without opening a file.
        public static string ValidatePath(string path, bool document)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.Any(c => char.IsControl(c))
                || path.IndexOfAny(new[] { '"', '<', '>', '|', '?', '*' }) >= 0
                || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
                || path.StartsWith(@"\??\", StringComparison.Ordinal) || path.Contains("://"))
                throw new ArgumentException("Only ordinary absolute Windows file paths are supported.");
            var normalized = path.Replace('/', '\\');
            var drive = normalized.Length >= 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '\\';
            var unc = normalized.StartsWith(@"\\", StringComparison.Ordinal);
            if (!drive && !unc) throw new ArgumentException("An absolute file path is required.");
            if (normalized.IndexOf(':', drive ? 2 : 0) >= 0)
                throw new ArgumentException("Alternate data streams and URI paths are not supported.");
            var segments = normalized.Substring(drive ? 3 : 2).Split('\\');
            if (unc && (segments.Length < 2 || string.IsNullOrEmpty(segments[0]) || string.IsNullOrEmpty(segments[1])))
                throw new ArgumentException("A UNC server and share are required.");
            foreach (var segment in segments.Where(s => s.Length != 0))
            {
                if (segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith(" ", StringComparison.Ordinal)) throw new ArgumentException("Non-canonical paths are not supported.");
                var name = segment.Split('.')[0].ToUpperInvariant();
                if (name == "CON" || name == "PRN" || name == "AUX" || name == "NUL" || name == "CONIN$" || name == "CONOUT$"
                    || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
                        && ((name[3] >= '1' && name[3] <= '9') || name[3] == '\u00b9' || name[3] == '\u00b2' || name[3] == '\u00b3')))
                    throw new ArgumentException("Device paths are not supported.");
            }
            if (document && !DocumentExtensions.Contains(Path.GetExtension(normalized)))
                throw new ArgumentException("This file type cannot be opened by the bridge.");
            return normalized;
        }
    }

    // Every SignalR HTTP request, including negotiation redirects, must remain on the paired origin.
    public sealed class OriginBoundHandler : DelegatingHandler
    {
        private readonly Uri _origin;
        public OriginBoundHandler(Uri origin) : this(origin, new HttpClientHandler { AllowAutoRedirect = false }) { }
        public OriginBoundHandler(Uri origin, HttpMessageHandler inner) : base(inner)
        {
            _origin = NativeBridgeSafety.ServerOrigin(origin.ToString());
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!NativeBridgeSafety.SameOrigin(_origin, request.RequestUri))
                throw new HttpRequestException("Bridge credentials cannot be sent to another origin.");
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                response.Dispose();
                throw new HttpRequestException("Bridge redirects are not permitted.");
            }
            return response;
        }
    }

    public sealed class BridgeRequestTracker
    {
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool TryBegin(string requestId)
        {
            Guid parsed;
            if (!Guid.TryParse(requestId, out parsed) || parsed == Guid.Empty) return false;
            lock (_seen)
            {
                // Never evict a request and risk executing it twice. Re-pair/restart after an unusually long session.
                if (_seen.Count >= 100000) return false;
                return _seen.Add(parsed.ToString("N"));
            }
        }
    }

    public sealed class NativeExecutionSession : IDisposable
    {
        private readonly object _sync = new object();
        private CancellationTokenSource _current = new CancellationTokenSource();
        private bool _disposed;
        public CancellationToken Token { get { lock (_sync) return _disposed ? new CancellationToken(true) : _current.Token; } }
        public void Disconnect() { lock (_sync) { if (!_disposed) _current.Cancel(); } }
        public void Reconnect()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _current.Cancel();
                _current.Dispose();
                _current = new CancellationTokenSource();
            }
        }
        public void EnsureExecutable(BridgeCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            NativeBridgeSafety.ValidateDeadline(command);
        }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _current.Cancel();
                _current.Dispose();
            }
        }
    }

    // Cancellation may drop queued work, but it must not release the caller's admission gate while COM is running.
    public sealed class NativeQueuedWork<T> : IDisposable
    {
        private readonly Func<T> _action;
        private readonly CancellationToken _token;
        private readonly CancellationTokenRegistration _registration;
        private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _state; // queued=0, running=1, cancelled=2, completed=3
        public Task<T> Completion { get { return _completion.Task; } }
        public NativeQueuedWork(Func<T> action, CancellationToken token)
        {
            _action = action;
            _token = token;
            _registration = token.Register(() =>
            {
                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) _completion.TrySetCanceled();
            });
        }
        public void Execute()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
            try { _token.ThrowIfCancellationRequested(); _completion.TrySetResult(_action()); }
            catch (Exception ex) { _completion.TrySetException(ex); }
            finally { Interlocked.Exchange(ref _state, 3); }
        }
        public void Dispose() { _registration.Dispose(); }
    }
}
