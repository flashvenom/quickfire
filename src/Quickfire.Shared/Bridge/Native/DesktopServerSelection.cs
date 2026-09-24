using System;
using System.Threading;
using System.Threading.Tasks;

namespace Quickfire.Shared.Bridge
{
    public static class DesktopServerSelection
    {
        // An empty preference means the bundled host must be started, even when its helper is paired.
        // Credentials identify the automation server; they never control web-host startup.
        public static Task<Uri> ResolveAsync(string explicitOrigin, Func<CancellationToken, Task<Uri>> ensureManagedHost,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return string.IsNullOrWhiteSpace(explicitOrigin)
                ? ensureManagedHost(cancellationToken)
                : Task.FromResult(NativeBridgeSafety.ServerOrigin(explicitOrigin));
        }

        public static string PreferenceFor(Uri selectedOrigin, Uri managedHostOrigin)
        {
            var origin = NativeBridgeSafety.ServerOrigin(selectedOrigin.AbsoluteUri);
            return managedHostOrigin != null && NativeBridgeSafety.SameOrigin(origin, managedHostOrigin)
                ? string.Empty : origin.AbsoluteUri;
        }
    }
}
