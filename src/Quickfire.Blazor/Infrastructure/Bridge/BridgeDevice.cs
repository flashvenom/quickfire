using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Quickfire.Blazor.Data;

namespace Quickfire.Blazor.Infrastructure.Bridge;

public static class BridgeScopes
{
    public const string Office = "office";
    public const string IncomingCall = "incoming-call";
}

[Index(nameof(UserId))]
public sealed class BridgeDevice
{
    public Guid Id { get; set; }
    [Required, MaxLength(450)] public string UserId { get; set; } = string.Empty;
    [ForeignKey(nameof(UserId)), JsonIgnore] public ApplicationUser User { get; set; } = null!;
    [Required, MaxLength(80)] public string Label { get; set; } = string.Empty;
    [Required, MaxLength(32)] public string Scope { get; set; } = string.Empty;
    [Required, MaxLength(32), JsonIgnore] public byte[] CredentialHash { get; set; } = [];
    [Required, MaxLength(256), JsonIgnore] public string SecurityStamp { get; set; } = string.Empty;
    public DateTime IssuedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public bool IsSelected { get; set; }
}

public sealed record BridgeIssuedCredential(BridgeDevice Device, string Credential);
