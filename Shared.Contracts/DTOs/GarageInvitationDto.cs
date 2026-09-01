namespace Shared.Contracts.DTOs;

/// <summary>
/// One entry in a garage's list of outstanding (pending) invitations, shown to staff managers so they
/// can see who has been invited and revoke an invitation. Returned by the garage invitation listing
/// and after creating an invitation.
/// </summary>
public class GarageInvitationDto
{
    /// <summary>The invitation id — used to revoke it.</summary>
    public required Guid Id { get; set; }

    /// <summary>The invitee's phone number.</summary>
    public required string Phone { get; set; }

    /// <summary>The id of the role the invitee will hold once they accept.</summary>
    public required Guid RoleId { get; set; }

    /// <summary>The name of the role the invitee will hold (e.g. "Owner", "Mechanic").</summary>
    public required string RoleName { get; set; }

    /// <summary>When the invitation was created.</summary>
    public required DateTime CreatedAt { get; set; }
}
