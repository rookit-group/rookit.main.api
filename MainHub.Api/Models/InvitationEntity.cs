namespace MainHub.Api.Models;

// Mirrors the `invitations` table in 001_initial.sql. One row = one PENDING invitation of the
// person with a given phone number to a garage, with the role they will hold once they accept.
// Built by hand from raw columns in InvitationRepository.Map(reader).
public class InvitationEntity
{
    public required Guid Id { get; set; }

    // FK to garages.id — the garage the person is invited to.
    public required Guid GarageId { get; set; }

    // The invitee's phone number. Matched against the accepting user's users.phone on accept.
    public required string Phone { get; set; }

    // The role the invitee will hold once they accept. The composite FK (role_id, garage_id)
    // guarantees the role belongs to the same garage as this invitation.
    public required Guid RoleId { get; set; }

    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
