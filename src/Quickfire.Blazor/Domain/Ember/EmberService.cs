using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.FluentUI.AspNetCore.Components;
using Quickfire.Blazor.Domain.Attachments.Models;
using Quickfire.Blazor.Domain.Shared.Helpers;
using Quickfire.Blazor.Infrastructure.Bridge;

namespace Quickfire.Blazor.Domain.Ember;

/// <summary>Dispatches desktop actions as the current authenticated Blazor user.</summary>
public sealed class EmberService(
    AuthenticationStateProvider authenticationStateProvider,
    IBridgeDispatcher dispatcher,
    IToastService toastService,
    ILogger<EmberService> logger)
{
    public async Task<bool> RunEmberFunction(string command, List<string> parameters, bool showFailure = true)
    {
        try
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated != true)
            {
                if (showFailure)
                    toastService.ShowWarning("Sign in before using a connected helper.");
                return false;
            }

            var response = await dispatcher.DispatchAsync(state.User, command, parameters);
            if (!response.Succeeded && showFailure)
                toastService.ShowWarning(response.Error ?? "The desktop helper could not complete this action. Check Connected helpers in your profile.");
            return response.Succeeded;
        }
        catch (Exception ex)
        {
            // Command parameters and helper responses can contain document and email content.
            logger.LogWarning("Desktop action {Command} failed ({ErrorType}).", command, ex.GetType().Name);
            if (showFailure)
                toastService.ShowWarning("The desktop helper is unavailable. Check Connected helpers in your profile and try again.");
            return false;
        }
    }

    public Task<bool> WindowsOpenFile(Attachment attachment) =>
        RunEmberFunction("Windows_OpenFile", [StringHelper.BuildWindowsPath(attachment, false)]);

    public Task<bool> WindowsOpenFolder(Attachment attachment)
    {
        if (string.IsNullOrWhiteSpace(attachment.LocalPath))
        {
            toastService.ShowWarning("This attachment does not have a local folder to open.");
            return Task.FromResult(false);
        }

        return RunEmberFunction("Windows_OpenFolder", [StringHelper.BuildWindowsPath(attachment, true)]);
    }
}
