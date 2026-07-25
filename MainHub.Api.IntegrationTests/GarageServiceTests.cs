using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Npgsql;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class GarageServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly GarageService _sut;

    public GarageServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);
        _sut = new GarageService(fixture.DataSource, _garages, _roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedInternalProfileAsync()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        return profile.Id;
    }

    // Happy path: one call seeds the garage, its wildcard-scoped Owner role, and the creator's
    // membership - and all three are readable after the transaction commits.
    [Fact]
    public async Task Create_seeds_garage_owner_role_and_membership()
    {
        var profileId = await SeedInternalProfileAsync();

        var result = await _sut.CreateAsync("Downtown Motors", profileId);

        var garage = await _garages.GetByIdAsync(result.Garage.Id);
        Assert.NotNull(garage);
        Assert.Equal("Downtown Motors", garage!.Name);

        var role = await _roles.GetByIdAsync(result.OwnerRole.Id);
        Assert.NotNull(role);
        Assert.Equal(GarageService.OwnerRoleName, role!.Name);
        Assert.Equal(garage.Id, role.GarageId);
        Assert.Equal(new[] { Scope.Wildcard }, role.Scopes);

        var membership = await _memberships.GetAsync(profileId, garage.Id);
        Assert.NotNull(membership);
        Assert.Equal(role.Id, membership!.RoleId);
    }

    // The owner role's wildcard scope must grant every known scope, including manage scopes,
    // without listing them - this is the whole point of seeding '*'.
    [Fact]
    public async Task Owner_role_grants_every_scope()
    {
        var profileId = await SeedInternalProfileAsync();

        var result = await _sut.CreateAsync("Grant Test Garage", profileId);

        foreach (var scope in Scope.All)
        {
            Assert.True(Scope.Grants(result.OwnerRole.Scopes, scope));
        }
    }

    // Rollback guard: a non-existent owner profile makes the membership INSERT fail its FK. The
    // garage and role written earlier in the same transaction must be rolled back, leaving nothing.
    [Fact]
    public async Task Create_rolls_back_entirely_when_owner_profile_does_not_exist()
    {
        var ghostProfileId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => _sut.CreateAsync("Ghost Garage", ghostProfileId));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);

        Assert.Equal(0, await CountAsync("garages"));
        Assert.Equal(0, await CountAsync("roles"));
        Assert.Equal(0, await CountAsync("internal_user_profiles_garages"));
    }

    private async Task<long> CountAsync(string table)
    {
        await using var cmd = _fixture.DataSource.CreateCommand($"SELECT COUNT(*) FROM {table}");
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
