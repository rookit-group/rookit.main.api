
namespace Shared.Contracts.DTOs;

public class AdminUserDetailsDto
{
  public required Guid Id { get; set; }
  public required string Name { get; set; }
  public string? Email { get; set; }
  public string? Phone { get; set; }
  public string? PictureUrl { get; set; }
  public required string ProviderId { get; set; }
  public required DateTime CreatedAt { get; set; }
}
