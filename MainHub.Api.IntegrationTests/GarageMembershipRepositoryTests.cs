using MainHub.Api.Authorization;
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

    [Fact]
    public async Task CountByRole_counts_only_members_holding_that_role()
    {
        var (profileId, garageId, roleId) = await SeedMembershipPrerequisitesAsync();
        await _sut.AddAsync(Factories.GarageMembership(profileId, garageId, roleId));

        // A second member in the same garage on a DIFFERENT role must not be counted.
        var otherUser = Factories.User();
        await _users.CreateAsync(otherUser);
        var otherProfile = Factories.InternalUserProfile(otherUser.Id);
        await _profiles.CreateAsync(otherProfile);
        var otherRole = Factories.Role(garageId, name: "Second Role");
        await _roles.CreateAsync(otherRole);
        await _sut.AddAsync(Factories.GarageMembership(otherProfile.Id, garageId, otherRole.Id));

        Assert.Equal(1, await _sut.CountByRoleAsync(roleId));
        Assert.Equal(1, await _sut.CountByRoleAsync(otherRole.Id));
    }

    [Fact]
    public async Task CountByRole_returns_zero_when_role_unused()
    {
        var (_, garageId, _) = await SeedMembershipPrerequisitesAsync();
        var unusedRole = Factories.Role(garageId, name: "Unused");
        await _roles.CreateAsync(unusedRole);

        Assert.Equal(0, await _sut.CountByRoleAsync(unusedRole.Id));
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

    // ----- CountMembersWithScope -----

    [Fact]
    public async Task CountMembersWithScope_counts_exact_scope_and_wildcard_holders()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);

        // Member 1: a role that explicitly grants staff:manage.
        await AddMemberAsync(garage.Id, Factories.Role(garage.Id, name: "Manager", scopes: [Scope.StaffManage]));
        // Member 2: the wildcard (owner) role - counts because '*' grants everything.
        await AddMemberAsync(garage.Id, Factories.Role(garage.Id, name: "Owner", scopes: [Scope.Wildcard], isSystem: true));
        // Member 3: a read-only role that does NOT grant staff:manage - excluded.
        await AddMemberAsync(garage.Id, Factories.Role(garage.Id, name: "Viewer", scopes: [Scope.StaffRead]));

        Assert.Equal(2, await _sut.CountMembersWithScopeAsync(garage.Id, Scope.StaffManage));
    }

    [Fact]
    public async Task CountMembersWithScope_is_scoped_to_the_garage()
    {
        var garageA = Factories.Garage();
        await _garages.CreateAsync(garageA);
        var garageB = Factories.Garage();
        await _garages.CreateAsync(garageB);

        await AddMemberAsync(garageA.Id, Factories.Role(garageA.Id, name: "Manager", scopes: [Scope.StaffManage]));
        await AddMemberAsync(garageB.Id, Factories.Role(garageB.Id, name: "Manager", scopes: [Scope.StaffManage]));

        Assert.Equal(1, await _sut.CountMembersWithScopeAsync(garageA.Id, Scope.StaffManage));
    }

    // ----- UpdateRole -----

    [Fact]
    public async Task UpdateRole_reassigns_member_and_returns_true()
    {
        var (profileId, garageId, roleId) = await SeedMembershipPrerequisitesAsync();
        await _sut.AddAsync(Factories.GarageMembership(profileId, garageId, roleId, updatedAt: null));
        var newRole = Factories.Role(garageId, name: "New Role");
        await _roles.CreateAsync(newRole);
        var updatedAt = new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc);

        var changed = await _sut.UpdateRoleAsync(profileId, garageId, newRole.Id, updatedAt);

        Assert.True(changed);
        var fetched = await _sut.GetAsync(profileId, garageId);
        Assert.NotNull(fetched);
        Assert.Equal(newRole.Id, fetched!.RoleId);
        Assert.Equal(updatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task UpdateRole_returns_false_when_member_missing()
    {
        var (_, garageId, roleId) = await SeedMembershipPrerequisitesAsync();

        var changed = await _sut.UpdateRoleAsync(Guid.NewGuid(), garageId, roleId, DateTime.UtcNow);

        Assert.False(changed);
    }

    // ----- Remove -----

    [Fact]
    public async Task Remove_deletes_membership_and_returns_true()
    {
        var (profileId, garageId, roleId) = await SeedMembershipPrerequisitesAsync();
        await _sut.AddAsync(Factories.GarageMembership(profileId, garageId, roleId));

        var removed = await _sut.RemoveAsync(profileId, garageId);

        Assert.True(removed);
        Assert.Null(await _sut.GetAsync(profileId, garageId));
    }

    [Fact]
    public async Task Remove_returns_false_when_member_missing()
    {
        var removed = await _sut.RemoveAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(removed);
    }

    // Creates a fresh user + internal profile in the given garage on the supplied role.
    private async Task AddMemberAsync(Guid garageId, MainHub.Api.Models.RoleEntity role)
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        await _roles.CreateAsync(role);
        await _sut.AddAsync(Factories.GarageMembership(profile.Id, garageId, role.Id));
    }
}
