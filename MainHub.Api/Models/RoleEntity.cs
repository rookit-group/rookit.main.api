namespace MainHub.Api.Models;

// Mirrors the `roles` table in 001_initial.sql. Roles are garage-scoped DATA:
// owner-defined bundles of scopes from the code catalog (see Authorization/Scope.cs).
// Built by hand from raw columns in RoleRepository.Map(reader).
public class RoleEntity
{
    public required Guid Id { get; set; }

    // FK to garages.id — a role belongs to exactly one garage.
    public required Guid GarageId { get; set; }

    // Owner-defined display name, unique within the garage (e.g. "Owner", "Senior Mechanic").
    public required string Name { get; set; }

    public string? Description { get; set; }

    // Subset of the code-defined scope catalog (Authorization/Scope.cs) this role grants,
    // or the wildcard "*" for a full-admin role (the seeded Owner role holds exactly ["*"]).
    public required List<string> Scopes { get; set; }

    // True for platform-seeded, immutable roles (currently only the per-garage "Owner" role).
    // The service refuses to edit or delete a system role. User-created roles are false.
    public bool IsSystem { get; set; }

    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
