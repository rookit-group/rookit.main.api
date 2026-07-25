using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class MembershipServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly MembershipService _sut;

    // Owner-level actor: holds the wildcard, so may assign any role.
    private static readonly string[] OwnerScopes = [Scope.Wildcard];

    public MembershipServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);
        _sut = new MembershipService(_profiles, _roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    // Creates a user (the identity being invited) and returns its id. Note: NO internal profile is
    // created here - the service is responsible for get-or-creating it.
    private async Task<Guid> SeedUserAsync()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        return user.Id;
    }

    private async Task<Guid> SeedRoleAsync(Guid garageId, string name, params string[] scopes)
    {
        var role = Factories.Role(garageId, name: name, scopes: [.. scopes]);
        await _roles.CreateAsync(role);
        return role.Id;
    }

    // ----- Invite -----

    [Fact]
    public async Task Invite_creates_profile_and_membership_for_a_new_staff_user()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);

        var membership = await _sut.InviteAsync(garageId, userId, roleId, OwnerScopes);

        // A profile was created on the fly...
        var profileId = await _profiles.GetIdByUserIdAsync(userId);
        Assert.NotNull(profileId);
        Assert.Equal(profileId!.Value, membership.InternalUserProfileId);
        // ...and the membership persisted with the requested role.
        var fetched = await _memberships.GetAsync(profileId.Value, garageId);
        Assert.NotNull(fetched);
        Assert.Equal(roleId, fetched!.RoleId);
    }

    [Fact]
    public async Task Invite_reuses_existing_profile()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var existingProfile = Factories.InternalUserProfile(userId);
        await _profiles.CreateAsync(existingProfile);
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);

        var membership = await _sut.InviteAsync(garageId, userId, roleId, OwnerScopes);

        Assert.Equal(existingProfile.Id, membership.InternalUserProfileId);
    }

    [Fact]
    public async Task Invite_blocks_a_duplicate_membership()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await _sut.InviteAsync(garageId, userId, roleId, OwnerScopes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.InviteAsync(garageId, userId, roleId, OwnerScopes));
    }

    [Fact]
    public async Task Invite_enforces_escalation_guard()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var roleId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        string[] actorScopes = [Scope.StaffRead]; // cannot grant staff:manage

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.InviteAsync(garageId, userId, roleId, actorScopes));
    }

    // Only a wildcard holder (an owner) can hand out the Owner role.
    [Fact]
    public async Task Invite_blocks_assigning_owner_role_unless_actor_holds_wildcard()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var everyConcreteScope = Scope.All.ToArray();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.InviteAsync(garageId, userId, ownerRoleId, everyConcreteScope));
    }

    [Fact]
    public async Task Invite_throws_when_role_missing()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.InviteAsync(garageId, userId, Guid.NewGuid(), OwnerScopes));
    }

    [Fact]
    public async Task Invite_blocks_role_from_a_different_garage()
    {
        var garageId = await SeedGarageAsync();
        var otherGarageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var foreignRoleId = await SeedRoleAsync(otherGarageId, "Foreign", Scope.GarageRead);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.InviteAsync(garageId, userId, foreignRoleId, OwnerScopes));
    }

    // ----- AssignRole -----

    [Fact]
    public async Task AssignRole_changes_the_members_role()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var seniorId = await SeedRoleAsync(garageId, "Senior", Scope.GarageRead, Scope.GarageManage);
        await _sut.InviteAsync(garageId, userId, mechanicId, OwnerScopes);

        var updated = await _sut.AssignRoleAsync(garageId, userId, seniorId, OwnerScopes);

        Assert.Equal(seniorId, updated.RoleId);
        var profileId = await _profiles.GetIdByUserIdAsync(userId);
        var fetched = await _memberships.GetAsync(profileId!.Value, garageId);
        Assert.Equal(seniorId, fetched!.RoleId);
    }

    [Fact]
    public async Task AssignRole_enforces_escalation_guard()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        await _sut.InviteAsync(garageId, userId, mechanicId, OwnerScopes);
        string[] actorScopes = [Scope.GarageRead]; // cannot grant staff:manage

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AssignRoleAsync(garageId, userId, managerId, actorScopes));
    }

    [Fact]
    public async Task AssignRole_throws_when_user_not_a_member()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.AssignRoleAsync(garageId, userId, roleId, OwnerScopes));
    }

    // Demotion lockout: the final staff-manager cannot be demoted to a role without staff:manage.
    [Fact]
    public async Task AssignRole_blocks_demoting_the_last_staff_manager()
    {
        var garageId = await SeedGarageAsync();
        var ownerUserId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await _sut.InviteAsync(garageId, ownerUserId, ownerRoleId, OwnerScopes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignRoleAsync(garageId, ownerUserId, mechanicId, OwnerScopes));
    }

    // Demotion is allowed while another staff-manager remains.
    [Fact]
    public async Task AssignRole_allows_demotion_when_another_staff_manager_exists()
    {
        var garageId = await SeedGarageAsync();
        var ownerUserId = await SeedUserAsync();
        var secondUserId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await _sut.InviteAsync(garageId, ownerUserId, ownerRoleId, OwnerScopes);
        await _sut.InviteAsync(garageId, secondUserId, managerId, OwnerScopes);

        // The second manager can be demoted because the owner still manages staff.
        var updated = await _sut.AssignRoleAsync(garageId, secondUserId, mechanicId, OwnerScopes);

        Assert.Equal(mechanicId, updated.RoleId);
    }

    // ----- Remove -----

    [Fact]
    public async Task Remove_deletes_the_membership()
    {
        var garageId = await SeedGarageAsync();
        var ownerUserId = await SeedUserAsync();
        var mechanicUserId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await _sut.InviteAsync(garageId, ownerUserId, ownerRoleId, OwnerScopes);
        await _sut.InviteAsync(garageId, mechanicUserId, mechanicId, OwnerScopes);

        await _sut.RemoveAsync(garageId, mechanicUserId);

        var profileId = await _profiles.GetIdByUserIdAsync(mechanicUserId);
        Assert.Null(await _memberships.GetAsync(profileId!.Value, garageId));
    }

    [Fact]
    public async Task Remove_blocks_removing_the_last_staff_manager()
    {
        var garageId = await SeedGarageAsync();
        var ownerUserId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        await _sut.InviteAsync(garageId, ownerUserId, ownerRoleId, OwnerScopes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RemoveAsync(garageId, ownerUserId));
    }

    [Fact]
    public async Task Remove_throws_when_user_not_a_member()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.RemoveAsync(garageId, userId));
    }
}
