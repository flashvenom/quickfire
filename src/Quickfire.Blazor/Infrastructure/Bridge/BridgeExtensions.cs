using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quickfire.Blazor.Domain.Ember;

namespace Quickfire.Blazor.Infrastructure.Bridge;

public static class BridgeExtensions
{
    public static IServiceCollection AddQuickfireBridge(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<BridgeConnections>();
        services.AddSingleton<BridgeDevices>();
        services.AddSingleton<BridgeDispatcher>();
        services.AddSingleton<IBridgeDispatcher>(sp => sp.GetRequiredService<BridgeDispatcher>());
        services.AddSingleton<BridgeNotifications>();
        services.AddSingleton<BridgeCallToasts>();
        services.AddHostedService(sp => sp.GetRequiredService<BridgeCallToasts>());
        services.AddSingleton<BridgeHubGuard>();
        services.AddHostedService<BridgeConnectionValidator>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, BridgeAuthentication>(BridgeAuthentication.SchemeName, _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(BridgeAuthentication.OfficePolicy, policy => policy.AddAuthenticationSchemes(BridgeAuthentication.SchemeName)
                .RequireAuthenticatedUser().RequireClaim(BridgeAuthentication.ScopeClaim, BridgeScopes.Office));
            options.AddPolicy(BridgeAuthentication.CallPolicy, policy => policy.AddAuthenticationSchemes(BridgeAuthentication.SchemeName)
                .RequireAuthenticatedUser().RequireClaim(BridgeAuthentication.ScopeClaim, BridgeScopes.IncomingCall));
        });
        services.AddSignalR()
            .AddHubOptions<EmberHub>(options => { options.AddFilter<BridgeHubGuard>(); options.MaximumParallelInvocationsPerClient = 1; })
            .AddHubOptions<NotificationHub>(options => { options.AddFilter<BridgeHubGuard>(); options.MaximumParallelInvocationsPerClient = 1; options.MaximumReceiveMessageSize = 4096; });
        return services;
    }

    public static IEndpointRouteBuilder MapQuickfireBridge(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<EmberHub>("/emberHub", options => options.CloseOnAuthenticationExpiration = true);
        endpoints.MapHub<NotificationHub>("/notificationHub", options => options.CloseOnAuthenticationExpiration = true);
        return endpoints;
    }
}

public sealed class BridgeConnectionValidator(BridgeDevices devices, BridgeConnections connections, TimeProvider time,
    ILogger<BridgeConnectionValidator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
            foreach (var connection in connections.Snapshot())
            {
                try { await devices.ValidateAsync(connection.DeviceId, connection.UserId, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    // Fail closed if credentials cannot be revalidated. Never log secrets or database details.
                    connections.Abort(connection.DeviceId);
                    logger.LogWarning("Disconnected a helper because its credential could not be revalidated.");
                }
            }
        }
    }
}
