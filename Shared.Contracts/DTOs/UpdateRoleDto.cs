namespace Shared.Contracts.DTOs;

/// <summary>
/// Request body for updating an existing garage role. Replaces the role's name, description and
/// scope set. System roles cannot be updated.
/// </summary>
public class UpdateRoleDto
{
    /// <summary>Display name for the role, unique within the garage.</summary>
    public required string Name { get; set; }

    /// <summary>Optional human-readable description.</summary>
    public string? Description { get; set; }

    /// <summary>The permission scopes to grant. Must all belong to the scope catalog.</summary>
    public required IReadOnlyList<string> Scopes { get; set; }
}
