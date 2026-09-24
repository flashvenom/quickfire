using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quickfire.Blazor.Data;

namespace Quickfire.Blazor.Infrastructure.Bridge;

// One instance per web host; all database work uses short-lived contexts.
public sealed class BridgeDevices(
    IDbContextFactory<ApplicationDbContext> factory,
    BridgeConnections connections,
    TimeProvider time,
    IOptions<IdentityOptions> identityOptions)
{
    private readonly SemaphoreSlim _changes = new(1, 1);

    public async Task<ApplicationUser> RequireUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("Sign in to manage or use connected helpers.");
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user == null || string.IsNullOrEmpty(stamp) || stamp != user.SecurityStamp || IsLockedOut(user))
            throw new UnauthorizedAccessException("Your sign-in has expired. Sign in again.");
        return user;
    }

    public async Task<List<BridgeDevice>> ListAsync(ClaimsPrincipal principal)
    {
        var user = await RequireUserAsync(principal);
        await using var db = await factory.CreateDbContextAsync();
        return await db.BridgeDevices.AsNoTracking().Where(x => x.UserId == user.Id).OrderByDescending(x => x.IssuedUtc).ToListAsync();
    }

    public async Task<BridgeIssuedCredential> IssueAsync(ClaimsPrincipal principal, string label, string scope)
    {
        var user = await RequireUserAsync(principal);
        label = label?.Trim() ?? string.Empty;
        if (label.Length is < 1 or > 80)
            throw new ArgumentException("Use a helper name between 1 and 80 characters.", nameof(label));
        if (scope != BridgeScopes.Office && scope != BridgeScopes.IncomingCall)
            throw new ArgumentException("Choose Office helper or incoming calls.", nameof(scope));
        await _changes.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var now = time.GetUtcNow().UtcDateTime;
            if (await db.BridgeDevices.CountAsync(x => x.UserId == user.Id && x.RevokedUtc == null && x.ExpiresUtc > now) >= 20)
                throw new InvalidOperationException("Revoke an unused helper before adding another (limit: 20).");
            var secret = RandomNumberGenerator.GetBytes(32);
            var device = new BridgeDevice
            {
                Id = Guid.NewGuid(), UserId = user.Id, Label = label, Scope = scope,
                CredentialHash = SHA256.HashData(secret), SecurityStamp = user.SecurityStamp!,
                IssuedUtc = now, ExpiresUtc = now.AddDays(90),
                IsSelected = scope == BridgeScopes.Office && !await db.BridgeDevices.AnyAsync(x => x.UserId == user.Id && x.Scope == BridgeScopes.Office)
            };
            // Only the first Office pairing is selected automatically. Replacement or switching is explicit.
            if (device.IsSelected)
                await ClearSelectionAsync(db, user.Id);
            db.BridgeDevices.Add(device);
            await db.SaveChangesAsync();
            var credential = device.Id.ToString("N") + "." + WebEncoders.Base64UrlEncode(secret);
            CryptographicOperations.ZeroMemory(secret);
            return new(device, credential);
        }
        finally { _changes.Release(); }
    }

    public async Task SelectAsync(ClaimsPrincipal principal, Guid deviceId)
    {
        var user = await RequireUserAsync(principal);
        await _changes.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var device = await db.BridgeDevices.SingleOrDefaultAsync(x => x.Id == deviceId && x.UserId == user.Id);
            if (device == null || device.Scope != BridgeScopes.Office || !IsUsable(device, user))
                throw new InvalidOperationException("This Office helper is unavailable. Create a new credential if it expired.");
            await ClearSelectionAsync(db, user.Id);
            device.IsSelected = true;
            await db.SaveChangesAsync();
        }
        finally { _changes.Release(); }
    }

    public async Task RevokeAsync(ClaimsPrincipal principal, Guid deviceId)
    {
        var user = await RequireUserAsync(principal);
        await _changes.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var device = await db.BridgeDevices.SingleOrDefaultAsync(x => x.Id == deviceId && x.UserId == user.Id)
                ?? throw new InvalidOperationException("Helper not found.");
            device.RevokedUtc ??= time.GetUtcNow().UtcDateTime;
            device.IsSelected = false;
            await db.SaveChangesAsync();
            connections.Abort(deviceId);
        }
        finally { _changes.Release(); }
    }

    public async Task<BridgeDevice?> AuthenticateAsync(string credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credential) || credential.Length > 100)
            return null;
        var parts = credential.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id))
            return null;
        byte[] secret;
        try { secret = WebEncoders.Base64UrlDecode(parts[1]); }
        catch (FormatException) { return null; }
        if (secret.Length != 32)
            return null;
        var hash = SHA256.HashData(secret);
        CryptographicOperations.ZeroMemory(secret);
        var device = await ValidateAsync(id, cancellationToken: cancellationToken);
        return device != null && CryptographicOperations.FixedTimeEquals(hash, device.CredentialHash) ? device : null;
    }

    public async Task<BridgeDevice?> ValidateAsync(Guid deviceId, string? expectedUserId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var device = await db.BridgeDevices.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.Id == deviceId, cancellationToken);
        if (device == null || (expectedUserId != null && device.UserId != expectedUserId) || !IsUsable(device, device.User))
        {
            connections.Abort(deviceId);
            return null;
        }
        return device;
    }

    public async Task<BridgeDevice?> GetSelectedAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var selectedId = await db.BridgeDevices.Where(x => x.UserId == userId && x.IsSelected && x.Scope == BridgeScopes.Office)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        return selectedId.HasValue ? await ValidateAsync(selectedId.Value, userId, cancellationToken) : null;
    }

    private bool IsUsable(BridgeDevice device, ApplicationUser? user) => user != null
        && device.RevokedUtc == null && device.ExpiresUtc > time.GetUtcNow().UtcDateTime
        && !string.IsNullOrEmpty(device.SecurityStamp) && device.SecurityStamp == user.SecurityStamp && !IsLockedOut(user);

    private bool IsLockedOut(ApplicationUser user) => user.LockoutEnabled && user.LockoutEnd > time.GetUtcNow();

    private static async Task ClearSelectionAsync(ApplicationDbContext db, string userId)
    {
        foreach (var selected in await db.BridgeDevices.Where(x => x.UserId == userId && x.IsSelected).ToListAsync())
            selected.IsSelected = false;
    }
}
