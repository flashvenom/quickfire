using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Quickfire.Shared.Bridge;
using System.Text;

internal static class SurefireCall
{
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The incoming-call helper stores pairing with Windows account protection and requires Windows.");
            return 1;
        }
        string? credentialDirectory = null;
        if (args.Length >= 2 && args[0] == "--credential-directory")
        {
            credentialDirectory = args[1];
            args = args.Skip(2).ToArray();
        }
        var store = new NativeCredentialStore("call", credentialDirectory);
        try
        {
            if (args.Length == 1 && args[0] == "--pair")
            {
                if (Console.IsInputRedirected) throw new InvalidOperationException();
                Console.Write("Quickfire server origin: ");
                var server = Console.ReadLine() ?? "";
                Console.Write("Incoming-call device credential from your Quickfire profile: ");
                var token = ReadSecret();
                store.Save(server.Trim(), token.Trim());
                Console.WriteLine("Pairing saved for this Windows account. No call was sent.");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--forget")
            {
                store.Forget();
                Console.WriteLine("Pairing removed. Revoke the credential in Quickfire if it is no longer needed.");
                return 0;
            }
            if (args.Length < 1 || args.Length > 2 || args[0].StartsWith("--", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(args[0]) || args[0].Length > 80 || (args.Length == 2 && args[1].Length > 200))
            {
                Console.Error.WriteLine("Usage: Quickfire.Call [--credential-directory <directory>] --pair | --forget | <caller-number> [caller-name]");
                return 1;
            }
            var credential = store.Load();
            if (credential == null)
            {
                Console.Error.WriteLine("Helper is not paired. Run Quickfire.Call --pair interactively first.");
                return 1;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var connection = new HubConnectionBuilder()
                .WithUrl(new Uri(credential.Origin, "notificationHub"), options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(credential.Token);
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => new OriginBoundHandler(credential.Origin);
                })
                .Build();
            await connection.StartAsync(timeout.Token);
            // Intentionally one send, without reconnect/retry: an uncertain result must never duplicate a call.
            await connection.InvokeAsync("SendIncomingCall", new CallInfo
            {
                CallerId = args[0], CallerName = args.Length == 2 ? args[1] : "Unknown Caller"
            }, timeout.Token);
            Console.WriteLine("Call notification accepted.");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("The helper could not complete the operation. Check the server, HTTPS certificate, and pairing in Quickfire. The notification was not retried.");
            return 1;
        }
    }

    private static string ReadSecret()
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException();
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar) && value.Length < 1024) value.Append(key.KeyChar);
        }
    }
}

public sealed class CallInfo
{
    public string CallerId { get; set; } = "";
    public string CallerName { get; set; } = "";
}
