using System.Net;
using Quickfire.Shared.Bridge;

namespace Quickfire.Founation.Tests;

public sealed class NativeBridgeTests
{
    [Fact]
    public async Task PairingTheManagedLocalServerStillStartsItOnRestart()
    {
        var managed = new Uri("http://127.0.0.1:5350/");
        var savedSelection = DesktopServerSelection.PreferenceFor(managed, managed);
        var starts = 0;
        var resolved = await DesktopServerSelection.ResolveAsync(savedSelection, _ =>
        {
            starts++;
            return Task.FromResult(managed);
        }, CancellationToken.None);
        Assert.Empty(savedSelection);
        Assert.Equal(1, starts);
        Assert.Equal(managed, resolved);
    }

    [Fact]
    public async Task ExplicitRemoteSelectionSkipsManagedHostUntilSelectionIsCleared()
    {
        var local = new Uri("http://127.0.0.1:5350/");
        var remote = new Uri("https://quickfire.example/");
        var savedSelection = DesktopServerSelection.PreferenceFor(remote, local);
        var starts = 0;
        Task<Uri> StartHost(CancellationToken _) { starts++; return Task.FromResult(local); }
        Assert.Equal(remote, await DesktopServerSelection.ResolveAsync(savedSelection, StartHost, CancellationToken.None));
        Assert.Equal(0, starts);
        Assert.Equal(local, await DesktopServerSelection.ResolveAsync("", StartHost, CancellationToken.None));
        Assert.Equal(1, starts);
    }

    [Theory]
    [InlineData("https://quickfire.example")]
    [InlineData("http://localhost:5350")]
    [InlineData("http://127.0.0.1:5350")]
    [InlineData("http://[::1]:5350")]
    public void AcceptsSecureOriginsAndLiteralLoopback(string value)
        => Assert.Equal("/", NativeBridgeSafety.ServerOrigin(value).AbsolutePath);

    [Theory]
    [InlineData("http://quickfire.example")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://127.0.0.1.evil.example")]
    [InlineData("http://0.0.0.0:5350")]
    [InlineData("https://user:token@quickfire.example")]
    [InlineData("https://quickfire.example/path")]
    [InlineData("https://quickfire.example/?token=secret")]
    [InlineData("file:///C:/test.pdf")]
    public void RejectsUnsafePairingOrigins(string value)
        => Assert.Throws<ArgumentException>(() => NativeBridgeSafety.ServerOrigin(value));

    [Theory]
    [InlineData(@"C:\Documents\policy.pdf")]
    [InlineData(@"\\server\share\policy.docx")]
    public void AllowsOrdinaryAbsoluteDocumentPaths(string path)
        => Assert.Equal(path, NativeBridgeSafety.ValidatePath(path, true));

    [Theory]
    [InlineData(@"C:\Temp\run.exe")]
    [InlineData(@"C:\Temp\run.ps1")]
    [InlineData(@"C:\Temp\shortcut.lnk")]
    [InlineData(@"C:\Temp\link.url")]
    [InlineData(@"C:\Temp\run.cmd")]
    [InlineData(@"C:\Temp\macro.docm")]
    [InlineData(@"C:\Temp\policy.pdf:run.exe")]
    [InlineData(@"C:\Temp\policy.pdf.")]
    [InlineData(@"C:\Temp\..\policy.pdf")]
    [InlineData(@"\\?\C:\Temp\policy.pdf")]
    [InlineData(@"\\.\pipe\policy.pdf")]
    [InlineData(@"C:\Temp\NUL.pdf")]
    [InlineData(@"C:\Temp\COM1.pdf")]
    [InlineData(@"C:\Temp\CONOUT$.pdf")]
    [InlineData("https://quickfire.example/policy.pdf")]
    [InlineData("file:///C:/policy.pdf")]
    [InlineData("relative.pdf")]
    [InlineData("C:\\Temp\\policy.pdf\" /malicious")]
    public void RejectsExecutableScriptUriAndDevicePaths(string path)
        => Assert.Throws<ArgumentException>(() => NativeBridgeSafety.ValidatePath(path, true));

    [Fact]
    public void ValidatesExactCommandsAndCorrelatedRequestIds()
    {
        var command = new BridgeCommand { RequestId = Guid.NewGuid().ToString("N"), Command = "GetWordDocContents", ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(60) };
        NativeBridgeSafety.ValidateCommand(command);
        command.Command = "OutlookAnything";
        Assert.Throws<ArgumentException>(() => NativeBridgeSafety.ValidateCommand(command));
        command.Command = "GetWordDocContents";
        command.RequestId = "not-a-request";
        Assert.Throws<ArgumentException>(() => NativeBridgeSafety.ValidateCommand(command));
    }

    [Fact]
    public void MissingOrExpiredDeadlinesCannotStartNativeActions()
    {
        var command = new BridgeCommand { RequestId = Guid.NewGuid().ToString("N"), Command = "GetWordDocContents" };
        Assert.Throws<InvalidOperationException>(() => NativeBridgeSafety.ValidateCommand(command));
        command.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.Throws<InvalidOperationException>(() => NativeBridgeSafety.ValidateDeadline(command));
        command.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30).ToOffset(TimeSpan.FromHours(1));
        Assert.Throws<InvalidOperationException>(() => NativeBridgeSafety.ValidateDeadline(command));
    }

    [Fact]
    public void ReconnectCannotReviveWorkFromThePreviousConnection()
    {
        using var session = new NativeExecutionSession();
        var command = new BridgeCommand { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30) };
        var previous = session.Token;
        session.EnsureExecutable(command, previous);
        session.Disconnect();
        session.Reconnect();
        Assert.Throws<OperationCanceledException>(() => session.EnsureExecutable(command, previous));
        session.EnsureExecutable(command, session.Token);
        command.ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.Throws<InvalidOperationException>(() => session.EnsureExecutable(command, session.Token));
    }

    [Fact]
    public async Task CancellationDropsQueuedWorkWithoutExecutingIt()
    {
        using var cancellation = new CancellationTokenSource();
        var executed = false;
        using var work = new NativeQueuedWork<bool>(() => executed = true, cancellation.Token);
        cancellation.Cancel();
        work.Execute();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.Completion);
        Assert.False(executed);
    }

    [Fact]
    public async Task CancellationCannotReleaseAdmissionWhileNativeWorkIsRunning()
    {
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var work = new NativeQueuedWork<bool>(() => { started.Set(); finish.Wait(); return true; }, cancellation.Token);
        var execution = Task.Run(work.Execute);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            cancellation.Cancel();
            Assert.False(work.Completion.IsCompleted);
        }
        finally { finish.Set(); }
        await execution;
        Assert.True(await work.Completion);
    }

    [Fact]
    public void NeverExecutesTheSameRequestTwiceAcrossGuidFormats()
    {
        var tracker = new BridgeRequestTracker();
        var request = Guid.NewGuid();
        Assert.True(tracker.TryBegin(request.ToString("N")));
        Assert.False(tracker.TryBegin(request.ToString("D").ToUpperInvariant()));
        Assert.False(tracker.TryBegin(""));
    }

    [Fact]
    public async Task NeverForwardsACredentialToAnotherOrigin()
    {
        var inner = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(new OriginBoundHandler(new Uri("https://quickfire.example"), inner));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://other.example/emberHub"));
        Assert.Equal(0, inner.Requests);
        await client.GetAsync("https://quickfire.example/emberHub");
        Assert.Equal(1, inner.Requests);
    }

    [Fact]
    public async Task RedirectResponsesAreRejected()
    {
        var inner = new RecordingHandler(HttpStatusCode.Redirect);
        using var client = new HttpClient(new OriginBoundHandler(new Uri("https://quickfire.example"), inner));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://quickfire.example/emberHub"));
        Assert.Equal(1, inner.Requests);
    }

    [Fact]
    public void WindowsCredentialIsEncryptedAndBoundToOriginAndProfile()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI is verified in the Windows CI job.
        var directory = Path.Combine(Path.GetTempPath(), "Quickfire-native-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new NativeCredentialStore("tray", directory);
            var token = Guid.NewGuid().ToString("N") + ".synthetic-test-token";
            store.Save("https://quickfire.example", token);
            Assert.Equal(token, store.Load().Token);
            var path = Path.Combine(directory, "tray.pairing");
            var saved = File.ReadAllText(path);
            Assert.DoesNotContain(token, saved);
            File.Copy(path, Path.Combine(directory, "call.pairing"));
            Assert.Throws<InvalidOperationException>(() => new NativeCredentialStore("call", directory).Load());
            File.WriteAllText(path, saved.Replace("https://quickfire.example/", "https://other.example/", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => store.Load());
        }
        finally
        {
            var fullPath = Path.GetFullPath(directory);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
