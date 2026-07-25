namespace MainHub.Api.Models;

// Mirrors the `internal_user_profiles_garages` junction table in 001_initial.sql.
// One row = one internal user profile linked to one garage, holding a single role there.
// Built by hand from raw columns in GarageMembershipRepository.Map(reader).
public class GarageMembershipEntity
{
    // FK to internal_user_profiles.id.
    public required Guid InternalUserProfileId { get; set; }

    // FK to garages.id. Together with InternalUserProfileId forms the composite PK.
    public required Guid GarageId { get; set; }

    // The member's single role within THIS garage. Always set (NOT NULL in the DB): a member
    // can't exist without a role. A member can hold different roles across different garages.
    public required Guid RoleId { get; set; }

    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
