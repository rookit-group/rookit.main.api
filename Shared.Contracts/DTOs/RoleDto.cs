namespace Shared.Contracts.DTOs;

/// <summary>
/// A garage role: an owner-defined bundle of permission scopes that can be assigned to staff.
/// Returned by the role listing and after create/update.
/// </summary>
public class RoleDto
{
    /// <summary>Unique role identifier.</summary>
    public required Guid Id { get; set; }

    /// <summary>The garage this role belongs to.</summary>
    public required Guid GarageId { get; set; }

    /// <summary>Owner-defined display name, unique within the garage (e.g. "Senior Mechanic").</summary>
    public required string Name { get; set; }

    /// <summary>Optional human-readable description of the role.</summary>
    public string? Description { get; set; }

    /// <summary>The permission scopes this role grants (or the wildcard "*" for a full-admin role).</summary>
    public required IReadOnlyList<string> Scopes { get; set; }

    /// <summary>
    /// True for platform-seeded, immutable roles (currently only the per-garage "Owner" role).
    /// System roles cannot be edited or deleted; clients should present them as read-only.
    /// </summary>
    public required bool IsSystem { get; set; }
}
