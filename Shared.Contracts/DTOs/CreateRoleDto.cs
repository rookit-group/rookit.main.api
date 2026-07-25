namespace Shared.Contracts.DTOs;

/// <summary>
/// Request body for creating a new garage role. The set of assignable scopes is the code-defined
/// catalog; the caller may only grant scopes they themselves hold (enforced server-side).
/// </summary>
public class CreateRoleDto
{
    /// <summary>Display name for the role, unique within the garage.</summary>
    public required string Name { get; set; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>The permission scopes to grant. Must all belong to the scope catalog.</summary>
    public required IReadOnlyList<string> Scopes { get; set; }
}
