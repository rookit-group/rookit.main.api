namespace MainHub.Api.Models;

public class ExternalUserProfileEntity
{
    public required Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
