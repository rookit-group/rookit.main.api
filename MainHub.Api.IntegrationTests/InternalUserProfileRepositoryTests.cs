using MainHub.Api.Repositories;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class InternalUserProfileRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _sut;

    public InternalUserProfileRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _sut = new InternalUserProfileRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedUserAsync()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        return user.Id;
    }

    private async Task<int> CountProfilesAsync(Guid userId)
    {
        await using var cmd = _fixture.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM internal_user_profiles WHERE user_id = @user_id");
        cmd.Parameters.AddWithValue("user_id", userId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task EnsureAsync_CreatesProfile_OnFirstCall()
    {
        var userId = await SeedUserAsync();

        var profileId = await _sut.EnsureAsync(userId, DateTime.UtcNow);

        Assert.NotEqual(Guid.Empty, profileId);
        Assert.Equal(1, await CountProfilesAsync(userId));
        Assert.Equal(profileId, await _sut.GetIdByUserIdAsync(userId));
    }

    [Fact]
    public async Task EnsureAsync_IsIdempotent_ReturnsSameProfileWithoutDuplicating()
    {
        var userId = await SeedUserAsync();

        var first = await _sut.EnsureAsync(userId, DateTime.UtcNow);
        var second = await _sut.EnsureAsync(userId, DateTime.UtcNow);

        Assert.Equal(first, second);
        Assert.Equal(1, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task EnsureAsync_IsRaceSafe_ConcurrentCallsConvergeOnOneProfile()
    {
        var userId = await SeedUserAsync();

        // Fire several EnsureAsync calls in parallel to mimic concurrent first-logins (double-click /
        // parallel tabs). The UNIQUE(user_id) + ON CONFLICT DO NOTHING must collapse them to one profile.
        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => _sut.EnsureAsync(userId, DateTime.UtcNow)));

        Assert.Single(results.Distinct());
        Assert.Equal(1, await CountProfilesAsync(userId));
    }
}
