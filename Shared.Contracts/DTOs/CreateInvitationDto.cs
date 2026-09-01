namespace Shared.Contracts.DTOs;

/// <summary>
/// Request to invite a person to a garage by phone number with a role. The invitee does not need to
/// have signed in yet; they are addressed by phone and become a member when they accept. The caller
/// may only grant a role whose scopes they themselves hold (escalation guard, enforced server-side).
/// </summary>
public class CreateInvitationDto
{
    /// <summary>The invitee's phone number. Matched against the accepting user's account phone.</summary>
    public required string Phone { get; set; }

    /// <summary>The id of the role the invitee will hold once they accept. Must belong to this garage.</summary>
    public required Guid RoleId { get; set; }
}
