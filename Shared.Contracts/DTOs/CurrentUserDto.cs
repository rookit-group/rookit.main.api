namespace Shared.Contracts.DTOs;

/// <summary>
/// The authenticated internal (company-staff) user's own identity, resolved from the
/// InternalIdentityJwt. The garage-list view renders this as its header, and the user shares
/// <see cref="UserId"/> with a garage owner so they can be invited (it is the same id
/// <c>InviteStaffDto.UserId</c> expects).
/// </summary>
public class CurrentUserDto
{
    /// <summary>
    /// The internal user id (<c>users.id</c>). This is the value another member supplies when
    /// inviting this user to a garage.
    /// </summary>
    public required Guid UserId { get; set; }

    /// <summary>
    /// The user's display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The user's email, if known.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// The URL of the user's profile picture, if any.
    /// </summary>
    public string? PictureUrl { get; set; }
}
