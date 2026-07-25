using MainHub.Api.Repositories;
using Npgsql;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class GarageMembershipRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _sut;

    public GarageMembershipRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _sut = new GarageMembershipRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Creates a user + internal profile + garage + role and returns the ids needed to link a
    // membership. Keeps each test focused on the membership behaviour under test.
    private async Task<(Guid profileId, Guid garageId, Guid roleId)> SeedMembershipPrerequisitesAsync()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id);
        await _roles.CreateAsync(role);
        return (profile.Id, garage.Id, role.Id);
    }

    // AddAsync + GetAsync proves Map(reader) reads every column back correctly,
    // the single mapping test for GarageMembershipEntity.
    [Fact]
    public async Task Add_then_Get_roundtrips_every_column()
    {
        var (profileId, garageId, roleId) = await SeedMembershipPrerequisitesAsync();
        var membership = Factories.GarageMembership(
            profileId, garageId, roleId,
            updatedAt: new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc));

        await _sut.AddAsync(membership);
        var fetched = await _sut.GetAsync(profileId, garageId);

        Assert.NotNull(fetched);
        Assert.Equal(profileId, fetched!.InternalUserProfileId);
        Assert.Equal(garageId, fetched.GarageId);
        Assert.Equal(roleId, fetched.RoleId);
        Assert.Equal(membership.CreatedAt, fetched.CreatedAt);
        Assert.Equal(membership.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Add_persists_null_updated_at()
    {
        var (profileId, garageId, roleId) = await SeedMembershipPrerequisitesAsync();
        await _sut.AddAsync(Factories.GarageMembership(profileId, garageId, roleId, updatedAt: null));

        var fetched = await _sut.GetAsync(profileId, garageId);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.UpdatedAt);
    }

    [Fact]
    public async Task Get_returns_null_when_missing()
    {
        var missing = await _sut.GetAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(missing);
    }

    // Schema guard: the composite FK (role_id, garage_id) -> roles(id, garage_id) must reject a
    // role that belongs to a DIFFERENT garage, so a role assignment can never cross garages.
    [Fact]
    public async Task Add_rejects_role_from_a_different_garage()
    {
        var (profileId, garageId, _) = await SeedMembershipPrerequisitesAsync();

        // A second garage with its own role; assigning it to a membership in the first garage
        // must fail the composite foreign key.
        var otherGarage = Factories.Garage();
        await _garages.CreateAsync(otherGarage);
        var otherRole = Factories.Role(otherGarage.Id);
        await _roles.CreateAsync(otherRole);

        var membership = Factories.GarageMembership(profileId, garageId, otherRole.Id);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => _sut.AddAsync(membership));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }
}
