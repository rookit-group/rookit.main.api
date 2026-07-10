namespace MainHub.Api.Models;

public class UserEntity
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public required string ProviderId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime? UpdatedAt { get; set; }
    public required string? Phone { get; set; }
    public required string? PictureUrl { get; set; }
}
