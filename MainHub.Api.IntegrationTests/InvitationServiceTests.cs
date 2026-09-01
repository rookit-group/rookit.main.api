using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class InvitationServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly InvitationRepository _invitations;
    private readonly InvitationService _sut;

    // Owner-level actor: holds the wildcard, so may grant any role.
    private static readonly string[] OwnerScopes = [Scope.Wildcard];

    public InvitationServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);
        _invitations = new InvitationRepository(fixture.DataSource);
        _sut = new InvitationService(
            fixture.DataSource, _invitations, _profiles, _users, _roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    private async Task<Guid> SeedRoleAsync(Guid garageId, string name, params string[] scopes)
    {
        var role = Factories.Role(garageId, name: name, scopes: [.. scopes]);
        await _roles.CreateAsync(role);
        return role.Id;
    }

    // Creates a signed-in user (with internal profile) and returns their id.
    private async Task<Guid> SeedUserAsync(string phone = "+1234567890")
    {
        var user = Factories.User(phone: phone);
        await _users.CreateAsync(user);
        await _profiles.CreateAsync(Factories.InternalUserProfile(user.Id));
        return user.Id;
    }

    // ----- Invite -----

    [Fact]
    public async Task Invite_creates_a_pending_invitation()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);

        var invitation = await _sut.InviteAsync(garageId, "+15551234", roleId, OwnerScopes);

        var fetched = await _invitations.GetByIdAsync(invitation.Id);
        Assert.NotNull(fetched);
        Assert.Equal(garageId, fetched!.GarageId);
        Assert.Equal("+15551234", fetched.Phone);
        Assert.Equal(roleId, fetched.RoleId);
    }

    [Fact]
    public async Task Invite_trims_the_phone_number()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);

        var invitation = await _sut.InviteAsync(garageId, "  +15551234  ", roleId, OwnerScopes);

        var fetched = await _invitations.GetByIdAsync(invitation.Id);
        Assert.Equal("+15551234", fetched!.Phone);
    }

    [Fact]
    public async Task Invite_blocks_a_duplicate_pending_invitation()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        await _sut.InviteAsync(garageId, "+15551234", roleId, OwnerScopes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.InviteAsync(garageId, "+15551234", roleId, OwnerScopes));
    }

    [Fact]
    public async Task Invite_enforces_escalation_guard()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        string[] actorScopes = [Scope.StaffRead]; // cannot grant staff:manage

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.InviteAsync(garageId, "+15551234", roleId, actorScopes));
    }

    [Fact]
    public async Task Invite_throws_when_role_missing()
    {
        var garageId = await SeedGarageAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.InviteAsync(garageId, "+15551234", Guid.NewGuid(), OwnerScopes));
    }

    [Fact]
    public async Task Invite_blocks_a_role_from_a_different_garage()
    {
        var garageId = await SeedGarageAsync();
        var otherGarageId = await SeedGarageAsync();
        var foreignRoleId = await SeedRoleAsync(otherGarageId, "Foreign", Scope.GarageRead);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.InviteAsync(garageId, "+15551234", foreignRoleId, OwnerScopes));
    }

    // ----- Revoke -----

    [Fact]
    public async Task Revoke_deletes_the_invitation()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var invitation = await _sut.InviteAsync(garageId, "+15551234", roleId, OwnerScopes);

        await _sut.RevokeAsync(garageId, invitation.Id);

        Assert.Null(await _invitations.GetByIdAsync(invitation.Id));
    }

    [Fact]
    public async Task Revoke_throws_when_missing()
    {
        var garageId = await SeedGarageAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.RevokeAsync(garageId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Revoke_throws_when_invitation_belongs_to_another_garage()
    {
        var garageId = await SeedGarageAsync();
        var otherGarageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var invitation = await _sut.InviteAsync(garageId, "+15551234", roleId, OwnerScopes);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.RevokeAsync(otherGarageId, invitation.Id));

        // The invitation is untouched.
        Assert.NotNull(await _invitations.GetByIdAsync(invitation.Id));
    }

    // ----- Accept -----

    [Fact]
    public async Task Accept_makes_the_user_a_member_and_consumes_the_invitation()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var userId = await SeedUserAsync("+15559999");
        var invitation = await _sut.InviteAsync(garageId, "+15559999", roleId, OwnerScopes);

        await _sut.AcceptAsync(invitation.Id, userId);

        // The membership now exists with the invited role...
        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        var membership = await _memberships.GetAsync(profileId, garageId);
        Assert.NotNull(membership);
        Assert.Equal(roleId, membership!.RoleId);
        // ...and the invitation is gone.
        Assert.Null(await _invitations.GetByIdAsync(invitation.Id));
    }

    [Fact]
    public async Task Accept_matches_on_a_phone_typed_with_surrounding_spaces()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var userId = await SeedUserAsync("  +15559999  ");
        var invitation = await _sut.InviteAsync(garageId, "+15559999", roleId, OwnerScopes);

        await _sut.AcceptAsync(invitation.Id, userId);

        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        Assert.NotNull(await _memberships.GetAsync(profileId, garageId));
    }

    [Fact]
    public async Task Accept_throws_when_the_invitation_is_missing()
    {
        var userId = await SeedUserAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.AcceptAsync(Guid.NewGuid(), userId));
    }

    [Fact]
    public async Task Accept_throws_when_the_phone_does_not_match()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var userId = await SeedUserAsync("+10000000");
        var invitation = await _sut.InviteAsync(garageId, "+15559999", roleId, OwnerScopes);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.AcceptAsync(invitation.Id, userId));

        // The invitation is left intact for its real recipient.
        Assert.NotNull(await _invitations.GetByIdAsync(invitation.Id));
    }

    [Fact]
    public async Task Accept_throws_when_the_user_has_no_phone()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var user = Factories.User(phone: null);
        await _users.CreateAsync(user);
        await _profiles.CreateAsync(Factories.InternalUserProfile(user.Id));
        var invitation = await _sut.InviteAsync(garageId, "+15559999", roleId, OwnerScopes);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.AcceptAsync(invitation.Id, user.Id));
    }

    [Fact]
    public async Task Accept_throws_and_clears_the_invitation_when_already_a_member()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.GarageRead);
        var userId = await SeedUserAsync("+15559999");
        // The user is already a member.
        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        await _memberships.AddAsync(Factories.GarageMembership(profileId, garageId, roleId));
        var invitation = await _sut.InviteAsync(garageId, "+15559999", roleId, OwnerScopes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AcceptAsync(invitation.Id, userId));

        // The stale invitation is cleared even though accept failed.
        Assert.Null(await _invitations.GetByIdAsync(invitation.Id));
    }
}
