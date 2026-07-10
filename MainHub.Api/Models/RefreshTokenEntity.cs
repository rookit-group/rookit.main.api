namespace MainHub.Api.Models;

public class RefreshTokenEntity
{
    public required Guid Id { get; set; }
    public required string Token { get; set; }
    public required Guid UserId { get; set; }
    public required string ProviderId { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required DateTime CreatedAt { get; set; }
    public bool IsRevoked { get; set; } = false;
}
