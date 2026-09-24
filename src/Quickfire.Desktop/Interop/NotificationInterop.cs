using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace Quickfire.Desktop.Interop;

public static class NotificationInterop
{
    public static async Task ShowTrayNotificationAsync(IReadOnlyList<string> parameters, ILogger logger, CancellationToken cancellationToken = default)
    {
        if (parameters is null || parameters.Count < 2)
        {
            throw new ArgumentException("A title and notification text are required.");
        }

        var title = parameters[0];
        var message = parameters[1];
        var text = string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}";

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Toast.Make(text, ToastDuration.Long).Show(cancellationToken);
            }
            catch
            {
                throw new InvalidOperationException("Unable to display the native notification.");
            }
        });
    }
}
