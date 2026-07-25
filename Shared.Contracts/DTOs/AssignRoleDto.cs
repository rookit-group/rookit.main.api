namespace Shared.Contracts.DTOs;

/// <summary>
/// Request to change an existing garage member's role.
/// </summary>
public class AssignRoleDto
{
    /// <summary>The id of the new role to assign. Must belong to this garage.</summary>
    public required Guid RoleId { get; set; }
}
