namespace Shared.Contracts.DTOs;

/// <summary>
/// One entry in the list of invitations addressed to the signed-in user's phone number, shown so they
/// can choose which garage to join. Carries the garage identity plus the role they would hold, and the
/// invitation id used to accept it.
/// </summary>
public class MyInvitationDto
{
    /// <summary>The invitation id — passed to the accept endpoint.</summary>
    public required Guid Id { get; set; }

    /// <summary>The id of the garage the user is invited to.</summary>
    public required Guid GarageId { get; set; }

    /// <summary>The garage's display name.</summary>
    public required string GarageName { get; set; }

    /// <summary>The id of the role the user would hold once they accept.</summary>
    public required Guid RoleId { get; set; }

    /// <summary>The name of the role the user would hold (e.g. "Owner", "Mechanic").</summary>
    public required string RoleName { get; set; }

    /// <summary>When the invitation was created.</summary>
    public required DateTime CreatedAt { get; set; }
}
