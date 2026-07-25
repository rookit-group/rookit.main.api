using System.Security.Claims;
using MainHub.Api.Authorization;
using MainHub.Api.Config;
using MainHub.Api.Endpoints;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Exercises the staff endpoint handlers directly against a real database (there is no HTTP harness
// in this suite). The RequireScope authorization and DTO validation are framework/filter behaviour
// tested elsewhere; here we assert the handlers' service wiring and their exception-to-status-code
// mapping, including the guards (invitee must exist, escalation, duplicate, last-staff-manager). The
// actor's scopes are supplied via the principal, mirroring how they arrive in the garage token.
[Collection("Postgres")]
public class StaffEndpointsTests : IAsyncLifetime
{
    private const string SecretKey = "test-secret-key-that-is-at-least-32-bytes-long!!";

    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly TokenService _tokenService;
    private readonly MembershipService _membershipService;

    public StaffEndpointsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);

        _tokenService = new TokenService(
            Options.Create(new JwtSettings { SecretKey = SecretKey, Issuer = "m", Audience = "m" }),
            Options.Create(new AdminJwtSettings { SecretKey = SecretKey, Issuer = "a", Audience = "a" }),
            Options.Create(new InternalIdentityJwtSettings { SecretKey = SecretKey, Issuer = "i", Audience = "i" }),
            Options.Create(new GarageJwtSettings { SecretKey = SecretKey, Issuer = "g", Audience = "g" }));

        _membershipService = new MembershipService(_profiles, _roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static ClaimsPrincipal PrincipalWithScopes(params string[] scopes) =>
        new(new ClaimsIdentity([new Claim(GarageContext.ScopeClaim, string.Join(' ', scopes))], "test"));

    private static readonly ClaimsPrincipal Owner = PrincipalWithScopes(Scope.Wildcard);

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

    // A registered user with an internal profile, but NOT a member of any garage.
    private async Task<Guid> SeedUserAsync(string name = "Test User")
    {
        var user = Factories.User(name: name);
        await _users.CreateAsync(user);
        await _profiles.CreateAsync(Factories.InternalUserProfile(user.Id));
        return user.Id;
    }

    private async Task<Guid> SeedMemberAsync(Guid garageId, Guid roleId, string name = "Member")
    {
        var userId = await SeedUserAsync(name);
        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        await _memberships.AddAsync(Factories.GarageMembership(profileId, garageId, roleId));
        return userId;
    }

    // ----- List -----

    [Fact]
    public async Task List_returns_the_garage_members()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        await SeedMemberAsync(garageId, roleId, "Alice");

        var result = await StaffEndpoints.ListStaffAsync(garageId, _memberships);

        var ok = Assert.IsType<Ok<List<StaffMemberDto>>>(result);
        var member = Assert.Single(ok.Value!);
        Assert.Equal("Alice", member.Name);
        Assert.Equal("Mechanic", member.RoleName);
        Assert.Equal(roleId, member.RoleId);
    }

    // ----- Invite -----

    [Fact]
    public async Task Invite_adds_an_existing_user_and_returns_created()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedUserAsync("Newcomer");
        var dto = new InviteStaffDto { UserId = userId, RoleId = roleId };

        var result = await StaffEndpoints.InviteStaffAsync(
            garageId, dto, Owner, _tokenService, _membershipService, _memberships);

        var created = Assert.IsType<Created<StaffMemberDto>>(result);
        Assert.Equal(userId, created.Value!.UserId);
        Assert.Equal("Newcomer", created.Value.Name);
        Assert.Equal("Mechanic", created.Value.RoleName);

        // The member is now listed in the garage.
        var list = Assert.IsType<Ok<List<StaffMemberDto>>>(await StaffEndpoints.ListStaffAsync(garageId, _memberships));
        Assert.Contains(list.Value!, m => m.UserId == userId);
    }

    [Fact]
    public async Task Invite_returns_404_with_a_message_when_the_user_has_never_signed_in()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        // A user id that has no internal profile (never signed in).
        var dto = new InviteStaffDto { UserId = Guid.NewGuid(), RoleId = roleId };

        var result = await StaffEndpoints.InviteStaffAsync(
            garageId, dto, Owner, _tokenService, _membershipService, _memberships);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Contains("No such user", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Invite_returns_409_when_the_user_is_already_a_member()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedMemberAsync(garageId, roleId, "Existing");
        var dto = new InviteStaffDto { UserId = userId, RoleId = roleId };

        var result = await StaffEndpoints.InviteStaffAsync(
            garageId, dto, Owner, _tokenService, _membershipService, _memberships);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task Invite_is_forbidden_when_the_actor_cannot_grant_the_role()
    {
        var garageId = await SeedGarageAsync();
        // Granting the owner (wildcard) role requires the actor to hold the wildcard themselves.
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var actor = PrincipalWithScopes(Scope.StaffManage); // can manage staff, but is not an owner
        var dto = new InviteStaffDto { UserId = Guid.NewGuid(), RoleId = ownerRoleId };

        var result = await StaffEndpoints.InviteStaffAsync(
            garageId, dto, actor, _tokenService, _membershipService, _memberships);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    // ----- Assign role -----

    [Fact]
    public async Task Assign_changes_the_members_role_and_returns_ok()
    {
        var garageId = await SeedGarageAsync();
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var seniorId = await SeedRoleAsync(garageId, "Senior", Scope.GarageRead);
        var userId = await SeedMemberAsync(garageId, mechanicId, "Mover");
        var dto = new AssignRoleDto { RoleId = seniorId };

        var result = await StaffEndpoints.AssignRoleAsync(
            garageId, userId, dto, Owner, _tokenService, _membershipService, _memberships);

        var ok = Assert.IsType<Ok<StaffMemberDto>>(result);
        Assert.Equal(seniorId, ok.Value!.RoleId);
        Assert.Equal("Senior", ok.Value.RoleName);
    }

    [Fact]
    public async Task Assign_returns_404_when_the_user_is_not_a_member()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var dto = new AssignRoleDto { RoleId = roleId };

        var result = await StaffEndpoints.AssignRoleAsync(
            garageId, Guid.NewGuid(), dto, Owner, _tokenService, _membershipService, _memberships);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Assign_is_forbidden_when_the_actor_cannot_grant_the_target_role()
    {
        var garageId = await SeedGarageAsync();
        var mechanicId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var userId = await SeedMemberAsync(garageId, mechanicId, "Target");
        var actor = PrincipalWithScopes(Scope.StaffManage);
        var dto = new AssignRoleDto { RoleId = ownerRoleId };

        var result = await StaffEndpoints.AssignRoleAsync(
            garageId, userId, dto, actor, _tokenService, _membershipService, _memberships);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task Assign_returns_409_when_demoting_the_last_staff_manager()
    {
        var garageId = await SeedGarageAsync();
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        var readOnlyId = await SeedRoleAsync(garageId, "Viewer", Scope.StaffRead);
        // The garage's only staff-manager; demoting them would lock the garage out.
        var userId = await SeedMemberAsync(garageId, managerId, "OnlyManager");
        var dto = new AssignRoleDto { RoleId = readOnlyId };

        var result = await StaffEndpoints.AssignRoleAsync(
            garageId, userId, dto, Owner, _tokenService, _membershipService, _memberships);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    // ----- Remove -----

    [Fact]
    public async Task Remove_removes_the_member_and_returns_no_content()
    {
        var garageId = await SeedGarageAsync();
        // Two staff-managers so removing one is allowed by the last-manager guard.
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        await SeedMemberAsync(garageId, managerId, "KeepManager");
        var userId = await SeedMemberAsync(garageId, managerId, "RemoveMe");

        var result = await StaffEndpoints.RemoveStaffAsync(garageId, userId, _membershipService);

        Assert.IsType<NoContent>(result);
        var list = Assert.IsType<Ok<List<StaffMemberDto>>>(await StaffEndpoints.ListStaffAsync(garageId, _memberships));
        Assert.DoesNotContain(list.Value!, m => m.UserId == userId);
    }

    [Fact]
    public async Task Remove_returns_404_when_the_user_is_not_a_member()
    {
        var garageId = await SeedGarageAsync();

        var result = await StaffEndpoints.RemoveStaffAsync(garageId, Guid.NewGuid(), _membershipService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Remove_returns_409_when_removing_the_last_staff_manager()
    {
        var garageId = await SeedGarageAsync();
        var managerId = await SeedRoleAsync(garageId, "Manager", Scope.StaffManage);
        var userId = await SeedMemberAsync(garageId, managerId, "OnlyManager");

        var result = await StaffEndpoints.RemoveStaffAsync(garageId, userId, _membershipService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }
}
