using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Quickfire.Tray;

namespace Quickfire.Desktop.Interop;

public static class MessagingInterop
{
    public static async Task ShowStaffChatNotificationAsync(IReadOnlyList<string> parameters, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (parameters is null || parameters.Count < 3)
        {
            throw new ArgumentException("Staff notification parameters are incomplete.");
        }

        var senderName = parameters[0];
        var message = parameters[1];
        var senderFullName = parameters[2];

        var title = string.IsNullOrWhiteSpace(senderFullName)
            ? $"Staff message from {senderName}"
            : $"Staff message from {senderFullName}";

        await NotificationInterop.ShowTrayNotificationAsync(new[] { title, $"{senderName}: {message}" }, logger, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await BringWindowToFrontAsync(logger);
    }

    private static async Task BringWindowToFrontAsync(ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                var handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    WindowsControl.BringWindowToFront(handle);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to bring Quickfire Desktop window to front for staff chat notification.");
            }
        });
    }
}
