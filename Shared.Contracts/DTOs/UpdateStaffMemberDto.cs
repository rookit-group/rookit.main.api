namespace Shared.Contracts.DTOs;

/// <summary>
/// Request to update an existing garage member: their profile fields and the role they hold in this
/// garage. <see cref="Name"/> and <see cref="Email"/> are optional — when omitted (null) the
/// corresponding field is left unchanged. <see cref="RoleId"/> is always required.
/// </summary>
public class UpdateStaffMemberDto
{
    /// <summary>New display name for the member. Null leaves the current name unchanged.</summary>
    public string? Name { get; set; }

    /// <summary>New email for the member. Null leaves the current email unchanged.</summary>
    public string? Email { get; set; }

    /// <summary>The id of the role to assign. Must belong to this garage.</summary>
    public required Guid RoleId { get; set; }
}
