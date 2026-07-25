namespace Shared.Contracts.DTOs;

/// <summary>
/// Request to invite an existing user to a garage with a role. The user must already have signed in
/// at least once (so their internal staff profile exists); inviting an unknown user is rejected.
/// </summary>
public class InviteStaffDto
{
    /// <summary>The id of the user to invite. Must be an already-registered user.</summary>
    public required Guid UserId { get; set; }

    /// <summary>The id of the role to grant the invited member. Must belong to this garage.</summary>
    public required Guid RoleId { get; set; }
}
