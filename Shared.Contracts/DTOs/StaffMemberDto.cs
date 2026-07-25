namespace Shared.Contracts.DTOs;

/// <summary>
/// One entry in a garage's staff list: a member's user identity plus the role they hold in this
/// garage. Returned by the staff listing and after an invite or role change.
/// </summary>
public class StaffMemberDto
{
    /// <summary>The member's user id — used to target role changes and removal.</summary>
    public required Guid UserId { get; set; }

    /// <summary>The member's display name.</summary>
    public required string Name { get; set; }

    /// <summary>The member's email address, if known.</summary>
    public string? Email { get; set; }

    /// <summary>URL to the member's profile picture, if any.</summary>
    public string? PictureUrl { get; set; }

    /// <summary>The id of the role the member holds in this garage.</summary>
    public required Guid RoleId { get; set; }

    /// <summary>The name of the role the member holds in this garage (e.g. "Owner", "Mechanic").</summary>
    public required string RoleName { get; set; }
}
