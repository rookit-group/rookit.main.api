using MainHub.Api.Repositories;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class RefreshTokenRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly RefreshTokenRepository _sut;
    private readonly UserRepository _users;

    public RefreshTokenRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _sut = new RefreshTokenRepository(fixture.DataSource);
        _users = new UserRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> CreateUserAsync()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        return user.Id;
    }

    // Create + GetByToken is also the Map(reader) round-trip for this entity.
    [Fact]
    public async Task Create_then_GetByToken_roundtrips_every_column()
    {
        var userId = await CreateUserAsync();
        var token = Factories.RefreshToken(
            userId,
            token: "abc-123",
            providerId: "tg-99",
            expiresAt: new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            isRevoked: false);

        await _sut.CreateAsync(token);
        var fetched = await _sut.GetByTokenAsync("abc-123");

        Assert.NotNull(fetched);
        Assert.Equal(token.Id, fetched!.Id);
        Assert.Equal("abc-123", fetched.Token);
        Assert.Equal(userId, fetched.UserId);
        Assert.Equal("tg-99", fetched.ProviderId);
        Assert.Equal(token.ExpiresAt, fetched.ExpiresAt);
        Assert.Equal(token.CreatedAt, fetched.CreatedAt);
        Assert.False(fetched.IsRevoked);
    }

    [Fact]
    public async Task GetByToken_returns_null_when_missing()
    {
        Assert.Null(await _sut.GetByTokenAsync("nope"));
    }

    [Fact]
    public async Task Revoke_sets_is_revoked_true()
    {
        var userId = await CreateUserAsync();
        var token = Factories.RefreshToken(userId, isRevoked: false);
        await _sut.CreateAsync(token);

        await _sut.RevokeAsync(token.Id);

        var fetched = await _sut.GetByTokenAsync(token.Token);
        Assert.True(fetched!.IsRevoked);
    }

    [Fact]
    public async Task Revoke_missing_id_is_noop()
    {
        // Should not throw.
        await _sut.RevokeAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task DeleteExpired_removes_only_tokens_with_expires_at_before_now()
    {
        var userId = await CreateUserAsync();
        var expired1 = Factories.RefreshToken(userId, expiresAt: DateTime.UtcNow.AddDays(-2));
        var expired2 = Factories.RefreshToken(userId, expiresAt: DateTime.UtcNow.AddMinutes(-5));
        var valid = Factories.RefreshToken(userId, expiresAt: DateTime.UtcNow.AddDays(1));
        await _sut.CreateAsync(expired1);
        await _sut.CreateAsync(expired2);
        await _sut.CreateAsync(valid);

        await _sut.DeleteExpiredAsync();

        Assert.Null(await _sut.GetByTokenAsync(expired1.Token));
        Assert.Null(await _sut.GetByTokenAsync(expired2.Token));
        Assert.NotNull(await _sut.GetByTokenAsync(valid.Token));
    }

    // Deleting the user CASCADEs to refresh_tokens (FK ON DELETE CASCADE).
    [Fact]
    public async Task Deleting_user_cascades_to_tokens()
    {
        var userId = await CreateUserAsync();
        var token = Factories.RefreshToken(userId);
        await _sut.CreateAsync(token);

        await _users.DeleteAsync(userId);

        Assert.Null(await _sut.GetByTokenAsync(token.Token));
    }
}
