using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Quickfire.Blazor.Infrastructure.Bridge;

public sealed class BridgeAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BridgeDevices devices,
    IOptions<IdentityOptions> identityOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "QuickfireHelper";
    public const string DeviceClaim = "quickfire:device";
    public const string ScopeClaim = "quickfire:helper-scope";
    public const string OfficePolicy = "QuickfireOfficeHelper";
    public const string CallPolicy = "QuickfireIncomingCalls";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/emberHub") && !Request.Path.StartsWithSegments("/notificationHub"))
            return AuthenticateResult.NoResult();
        // The desktop host is intentionally HTTP on loopback. Never allow cleartext remote credentials.
        var host = Request.Host.Host.Trim('[', ']');
        var loopbackHost = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var hostAddress) && IPAddress.IsLoopback(hostAddress));
        if (!Request.IsHttps && !(loopbackHost && Context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)))
            return AuthenticateResult.Fail("Helpers require HTTPS outside loopback.");
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();
        var device = await devices.AuthenticateAsync(authorization[7..].Trim(), Context.RequestAborted);
        if (device == null)
            return AuthenticateResult.Fail("Invalid or expired helper credential.");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, device.UserId),
            new Claim(DeviceClaim, device.Id.ToString("N")),
            new Claim(ScopeClaim, device.Scope),
            new Claim(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType, device.SecurityStamp)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(device.ExpiresUtc, DateTimeKind.Utc)) }, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"Quickfire helpers\"";
        return Response.WriteAsJsonAsync(new
        {
            error = "helper_pairing_required",
            message = "Install an Quickfire 1.2 or later helper and pair it using a device credential from your signed-in Profile. If already paired, check credential expiry and the server's HTTPS address. Anonymous legacy helper connections are not supported."
        }, Context.RequestAborted);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new
        {
            error = "helper_scope_mismatch",
            message = "This device credential does not allow this helper function. Create the matching Office or incoming-call credential in your signed-in Profile and pair the helper again."
        }, Context.RequestAborted);
    }
}
