using System.ComponentModel.DataAnnotations;

namespace backlite.Models;

public sealed record ServerConnection(
    Guid Id,
    [Required] string Name,
    [Required] string Host,
    int Port,
    [Required] string Username,
    AuthKind AuthKind,
    string? Password,
    string? PrivateKeyPem,
    bool UseSudoForDiscovery,
    string[] AllowedRoots
)
{
    public ServerConnection() : this(
        Guid.NewGuid(),
        string.Empty,
        string.Empty,
        22,
        string.Empty,
        AuthKind.Password,
        null,
        null,
        false,
        Array.Empty<string>()
    ) { }
}

public enum AuthKind
{
    Password,
    SshKey
}