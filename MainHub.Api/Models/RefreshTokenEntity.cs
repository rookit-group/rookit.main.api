namespace MainHub.Api.Models;

// Shape is unchanged from Mongo - plain C# class, no BSON attributes needed
// since Npgsql maps rows to this manually (see RefreshTokenRepository.Map).
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
