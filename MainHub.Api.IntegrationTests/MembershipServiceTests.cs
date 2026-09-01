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
        _sut = new MembershipService(fixture.DataSource, _profiles, _users, _roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    // Creates a user AND their internal profile - i.e. someone who has signed in at least once.
    // Returns the user id.
    private async Task<Guid> SeedUserAsync()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        await _profiles.CreateAsync(Factories.InternalUserProfile(user.Id));
        return user.Id;
    }

    private async Task<Guid> SeedRoleAsync(Guid garageId, string name, params string[] scopes)
    {
        var role = Factories.Role(garageId, name: name, scopes: [.. scopes]);
        await _roles.CreateAsync(role);
        return role.Id;
    }

    // Directly links an existing user to a garage with a role. Memberships are created by the
    // invitation flow in production (see InvitationServiceTests); here we seed them straight through the
    // repository so these tests can focus on the role-change and removal behaviour.
    private async Task SeedMembershipAsync(Guid garageId, Guid userId, Guid roleId)
    {
        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        await _memberships.AddAsync(Factories.GarageMembership(profileId, garageId, roleId));
    }

    // ----- UpdateMember -----

    [Fact]
    public async Task UpdateMember_changes_the_members_role()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var seniorId = await SeedRoleAsync(garageId, "Senior", Scope.GarageRead, Scope.GarageManage);
        await SeedMembershipAsync(garageId, userId, mechanicId);

        var updated = await _sut.UpdateMemberAsync(garageId, userId, null, null, seniorId, OwnerScopes);

        Assert.Equal(seniorId, updated.RoleId);
        var profileId = await _profiles.GetIdByUserIdAsync(userId);
        var fetched = await _memberships.GetAsync(profileId!.Value, garageId);
        Assert.Equal(seniorId, fetched!.RoleId);
    }

    // The combined update also writes the member's identity (name/email) on the shared user row.
    [Fact]
    public async Task UpdateMember_updates_the_users_name_and_email()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await SeedMembershipAsync(garageId, userId, roleId);

        await _sut.UpdateMemberAsync(garageId, userId, "Renamed", "new@example.com", roleId, OwnerScopes);

        var user = await _users.GetByIdAsync(userId);
        Assert.NotNull(user);
        Assert.Equal("Renamed", user!.Name);
        Assert.Equal("new@example.com", user.Email);
    }

    // Null name/email leave the stored identity untouched (a pure role change).
    [Fact]
    public async Task UpdateMember_leaves_profile_unchanged_when_name_and_email_are_null()
    {
        var garageId = await SeedGarageAsync();
        var user = Factories.User(name: "Original");
        await _users.CreateAsync(user);
        await _profiles.CreateAsync(Factories.InternalUserProfile(user.Id));
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var seniorId = await SeedRoleAsync(garageId, "Senior", Scope.GarageRead, Scope.GarageManage);
        await SeedMembershipAsync(garageId, user.Id, mechanicId);

        await _sut.UpdateMemberAsync(garageId, user.Id, null, null, seniorId, OwnerScopes);

        var fetched = await _users.GetByIdAsync(user.Id);
        Assert.Equal("Original", fetched!.Name);
    }

    [Fact]
    public async Task UpdateMember_enforces_escalation_guard()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        await SeedMembershipAsync(garageId, userId, mechanicId);
        string[] actorScopes = [Scope.GarageRead]; // cannot grant staff:manage

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.UpdateMemberAsync(garageId, userId, null, null, managerId, actorScopes));
    }

    [Fact]
    public async Task UpdateMember_throws_when_user_not_a_member()
    {
        var garageId = await SeedGarageAsync();
        var userId = await SeedUserAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateMemberAsync(garageId, userId, null, null, roleId, OwnerScopes));
    }

    // Demotion lockout: the final staff-manager cannot be demoted to a role without staff:manage.
    [Fact]
    public async Task UpdateMember_blocks_demoting_the_last_staff_manager()
    {
        var garageId = await SeedGarageAsync();
        var ownerUserId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await SeedMembershipAsync(garageId, ownerUserId, ownerRoleId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateMemberAsync(garageId, ownerUserId, null, null, mechanicId, OwnerScopes));
    }

    // Demotion is allowed while another staff-manager remains.
    [Fact]
    public async Task UpdateMember_allows_demotion_when_another_staff_manager_exists()
    {
        var garageId = await SeedGarageAsync();
        var ownerUserId = await SeedUserAsync();
        var secondUserId = await SeedUserAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await SeedMembershipAsync(garageId, ownerUserId, ownerRoleId);
        await SeedMembershipAsync(garageId, secondUserId, managerId);

        // The second manager can be demoted because the owner still manages staff.
        var updated = await _sut.UpdateMemberAsync(garageId, secondUserId, null, null, mechanicId, OwnerScopes);

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
        await SeedMembershipAsync(garageId, ownerUserId, ownerRoleId);
        await SeedMembershipAsync(garageId, mechanicUserId, mechanicId);

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
        await SeedMembershipAsync(garageId, ownerUserId, ownerRoleId);

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
