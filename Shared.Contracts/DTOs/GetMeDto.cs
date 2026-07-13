namespace Shared.Contracts.DTOs;

/// <summary>
/// Represents a DTO for retrieving the current user's information.
/// </summary>
public class GetMeDto
{
  /// <summary>
  /// Represents the name of the user.
  /// </summary>
  public required string Name { get; set; }

  /// <summary>
  /// Represents the email of the user.
  /// </summary>
  public string? Email { get; set; }

  /// <summary>
  /// Represents the date and time when the user was last modified.
  /// </summary>
  public required DateTime? UpdatedAt { get; set; }

  /// <summary>
  ///  Represents the phone number of the user.
  /// </summary>
  public required string? Phone { get; set; }

  /// <summary>
  /// Represents the URL of the user's profile picture.
  /// </summary>
  public required string? PictureUrl { get; set; }


}