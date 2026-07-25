using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class RoleServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly RoleService _sut;

    // Owner-level actor: holds the wildcard, so may grant anything.
    private static readonly string[] OwnerScopes = [Scope.Wildcard];

    public RoleServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);
        _sut = new RoleService(_roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    // ----- Create -----

    [Fact]
    public async Task Create_persists_role_as_non_system_with_given_scopes()
    {
        var garageId = await SeedGarageAsync();

        var role = await _sut.CreateAsync(
            garageId, "Mechanic", "Handles repairs",
            [Scope.StaffRead, Scope.GarageRead],
            OwnerScopes);

        var fetched = await _roles.GetByIdAsync(role.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Mechanic", fetched!.Name);
        Assert.Equal(garageId, fetched.GarageId);
        Assert.Equal(new[] { Scope.StaffRead, Scope.GarageRead }, fetched.Scopes);
        // Owners create ordinary roles; only the platform seeds system roles.
        Assert.False(fetched.IsSystem);
    }

    // Escalation guard: an actor may not grant a scope they don't personally hold.
    [Fact]
    public async Task Create_blocks_granting_a_scope_the_actor_does_not_hold()
    {
        var garageId = await SeedGarageAsync();
        string[] actorScopes = [Scope.StaffRead]; // read only

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync(
            garageId, "Escalated", null, [Scope.StaffManage], actorScopes));
    }

    // Even an actor holding every CONCRETE scope cannot mint a wildcard (owner) role - only a
    // wildcard holder can. This is what stops a full-but-non-owner admin from creating a peer owner.
    [Fact]
    public async Task Create_blocks_granting_wildcard_unless_actor_holds_wildcard()
    {
        var garageId = await SeedGarageAsync();
        var everyConcreteScope = Scope.All.ToArray();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync(
            garageId, "New Owner", null, [Scope.Wildcard], everyConcreteScope));
    }

    [Fact]
    public async Task Create_allows_owner_to_grant_any_scope_including_wildcard()
    {
        var garageId = await SeedGarageAsync();

        var role = await _sut.CreateAsync(
            garageId, "Co-Owner", null, [Scope.Wildcard], OwnerScopes);

        Assert.Equal(new[] { Scope.Wildcard }, role.Scopes);
    }

    // ----- Update -----

    [Fact]
    public async Task Update_changes_fields_when_actor_authorized()
    {
        var garageId = await SeedGarageAsync();
        var created = await _sut.CreateAsync(garageId, "Mechanic", "old", [Scope.GarageRead], OwnerScopes);

        var updated = await _sut.UpdateAsync(
            garageId, created.Id, "Senior Mechanic", "new", [Scope.GarageRead, Scope.StaffManage], OwnerScopes);

        var fetched = await _roles.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Senior Mechanic", fetched!.Name);
        Assert.Equal("new", fetched.Description);
        Assert.Equal(new[] { Scope.GarageRead, Scope.StaffManage }, fetched.Scopes);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Update_throws_when_role_missing()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.UpdateAsync(
            Guid.NewGuid(), Guid.NewGuid(), "X", null, [Scope.GarageRead], OwnerScopes));
    }

    // A garage token only authorizes its own garage: addressing a role that exists but belongs to a
    // different garage must be treated as not-found, never edited.
    [Fact]
    public async Task Update_treats_a_role_from_another_garage_as_not_found()
    {
        var garageId = await SeedGarageAsync();
        var otherGarageId = await SeedGarageAsync();
        var foreignRole = await _sut.CreateAsync(otherGarageId, "Foreign", null, [Scope.GarageRead], OwnerScopes);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.UpdateAsync(
            garageId, foreignRole.Id, "Hijacked", null, [Scope.GarageRead], OwnerScopes));

        // The foreign role is untouched.
        var fetched = await _roles.GetByIdAsync(foreignRole.Id);
        Assert.Equal("Foreign", fetched!.Name);
    }

    [Fact]
    public async Task Update_enforces_escalation_guard()
    {
        var garageId = await SeedGarageAsync();
        var created = await _sut.CreateAsync(garageId, "Mechanic", null, [Scope.GarageRead], OwnerScopes);
        string[] actorScopes = [Scope.GarageRead]; // cannot grant StaffManage

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.UpdateAsync(
            garageId, created.Id, "Mechanic", null, [Scope.GarageRead, Scope.StaffManage], actorScopes));
    }

    // A platform-seeded system role (the Owner role) is immutable and must reject edits, even from
    // an owner-level actor, and the stored scopes must be untouched.
    [Fact]
    public async Task Update_blocks_editing_a_system_role()
    {
        var garageId = await SeedGarageAsync();
        var ownerRole = Factories.Role(garageId, name: "Owner", scopes: [Scope.Wildcard], isSystem: true);
        await _roles.CreateAsync(ownerRole);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateAsync(
            garageId, ownerRole.Id, "Renamed Owner", null, [Scope.GarageRead], OwnerScopes));

        var fetched = await _roles.GetByIdAsync(ownerRole.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Owner", fetched!.Name);
        Assert.Equal(new[] { Scope.Wildcard }, fetched.Scopes);
    }

    // ----- Delete -----

    [Fact]
    public async Task Delete_removes_role_when_unused()
    {
        var garageId = await SeedGarageAsync();
        var role = await _sut.CreateAsync(garageId, "Temp", null, [Scope.GarageRead], OwnerScopes);

        await _sut.DeleteAsync(garageId, role.Id);

        Assert.Null(await _roles.GetByIdAsync(role.Id));
    }

    [Fact]
    public async Task Delete_throws_when_role_missing()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.DeleteAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    // Cross-garage isolation: a role from another garage is not-found here and must survive.
    [Fact]
    public async Task Delete_treats_a_role_from_another_garage_as_not_found()
    {
        var garageId = await SeedGarageAsync();
        var otherGarageId = await SeedGarageAsync();
        var foreignRole = await _sut.CreateAsync(otherGarageId, "Foreign", null, [Scope.GarageRead], OwnerScopes);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.DeleteAsync(garageId, foreignRole.Id));
        Assert.NotNull(await _roles.GetByIdAsync(foreignRole.Id));
    }

    // In-use guard: a role held by at least one member cannot be deleted, and the role survives.
    [Fact]
    public async Task Delete_blocks_and_preserves_a_role_that_is_assigned()
    {
        var garageId = await SeedGarageAsync();
        var role = await _sut.CreateAsync(garageId, "Mechanic", null, [Scope.GarageRead], OwnerScopes);

        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        await _memberships.AddAsync(Factories.GarageMembership(profile.Id, garageId, role.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteAsync(garageId, role.Id));
        Assert.NotNull(await _roles.GetByIdAsync(role.Id));
    }

    // A platform-seeded system role cannot be deleted even when no member holds it.
    [Fact]
    public async Task Delete_blocks_and_preserves_a_system_role()
    {
        var garageId = await SeedGarageAsync();
        var ownerRole = Factories.Role(garageId, name: "Owner", scopes: [Scope.Wildcard], isSystem: true);
        await _roles.CreateAsync(ownerRole);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteAsync(garageId, ownerRole.Id));
        Assert.NotNull(await _roles.GetByIdAsync(ownerRole.Id));
    }

    // ----- List -----

    [Fact]
    public async Task ListByGarage_returns_the_garages_roles()
    {
        var garageId = await SeedGarageAsync();
        await _sut.CreateAsync(garageId, "Owner", null, [Scope.Wildcard], OwnerScopes);
        await _sut.CreateAsync(garageId, "Mechanic", null, [Scope.GarageRead], OwnerScopes);

        var roles = await _sut.ListByGarageAsync(garageId);

        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.Name == "Owner");
        Assert.Contains(roles, r => r.Name == "Mechanic");
    }
}
