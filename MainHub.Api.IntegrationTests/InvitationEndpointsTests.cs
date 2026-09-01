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

// Exercises the invitation endpoint handlers directly against a real database (there is no HTTP
// harness in this suite). RequireScope/identity authorization and DTO validation are framework/filter
// behaviour tested elsewhere; here we assert the handlers' service wiring and their exception-to-
// status-code mapping. Garage-side handlers read the actor's scopes from the principal (mirroring the
// garage token); invitee-side handlers read the userId claim (mirroring the identity token).
[Collection("Postgres")]
public class InvitationEndpointsTests : IAsyncLifetime
{
    private const string SecretKey = "test-secret-key-that-is-at-least-32-bytes-long!!";

    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly InvitationRepository _invitations;
    private readonly TokenService _tokenService;
    private readonly InvitationService _invitationService;

    public InvitationEndpointsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);
        _invitations = new InvitationRepository(fixture.DataSource);

        _tokenService = new TokenService(
            Options.Create(new JwtSettings { SecretKey = SecretKey, Issuer = "m", Audience = "m" }),
            Options.Create(new AdminJwtSettings { SecretKey = SecretKey, Issuer = "a", Audience = "a" }),
            Options.Create(new InternalIdentityJwtSettings { SecretKey = SecretKey, Issuer = "i", Audience = "i" }),
            Options.Create(new GarageJwtSettings { SecretKey = SecretKey, Issuer = "g", Audience = "g" }));

        _invitationService = new InvitationService(
            fixture.DataSource, _invitations, _profiles, _users, _roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Garage token surrogate: carries the actor's scopes in the scope claim.
    private static ClaimsPrincipal PrincipalWithScopes(params string[] scopes) =>
        new(new ClaimsIdentity([new Claim(GarageContext.ScopeClaim, string.Join(' ', scopes))], "test"));

    // Identity token surrogate: carries the acting user's id.
    private static ClaimsPrincipal PrincipalForUser(Guid userId) =>
        new(new ClaimsIdentity([new Claim("userId", userId.ToString())], "test"));

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

    private async Task<Guid> SeedUserAsync(string? phone = "+15559999")
    {
        var user = Factories.User(phone: phone);
        await _users.CreateAsync(user);
        await _profiles.CreateAsync(Factories.InternalUserProfile(user.Id));
        return user.Id;
    }

    // ----- List (garage side) -----

    [Fact]
    public async Task ListGarage_returns_the_pending_invitations()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        await _invitationService.InviteAsync(garageId, "+15551234", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.ListGarageInvitationsAsync(garageId, _invitations);

        var ok = Assert.IsType<Ok<List<GarageInvitationDto>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("+15551234", item.Phone);
        Assert.Equal("Mechanic", item.RoleName);
    }

    // ----- Create (garage side) -----

    [Fact]
    public async Task Create_returns_created_with_the_resolved_role_name()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var dto = new CreateInvitationDto { Phone = "+15551234", RoleId = roleId };

        var result = await InvitationEndpoints.CreateInvitationAsync(
            garageId, dto, Owner, _tokenService, _invitationService, _invitations);

        var created = Assert.IsType<Created<GarageInvitationDto>>(result);
        Assert.Equal("+15551234", created.Value!.Phone);
        Assert.Equal("Mechanic", created.Value.RoleName);
        Assert.Single(await _invitations.ListByGarageAsync(garageId));
    }

    [Fact]
    public async Task Create_returns_404_when_the_role_does_not_exist()
    {
        var garageId = await SeedGarageAsync();
        var dto = new CreateInvitationDto { Phone = "+15551234", RoleId = Guid.NewGuid() };

        var result = await InvitationEndpoints.CreateInvitationAsync(
            garageId, dto, Owner, _tokenService, _invitationService, _invitations);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Create_returns_409_on_a_duplicate_phone()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        await _invitationService.InviteAsync(garageId, "+15551234", roleId, [Scope.Wildcard]);
        var dto = new CreateInvitationDto { Phone = "+15551234", RoleId = roleId };

        var result = await InvitationEndpoints.CreateInvitationAsync(
            garageId, dto, Owner, _tokenService, _invitationService, _invitations);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task Create_is_forbidden_when_the_actor_cannot_grant_the_role()
    {
        var garageId = await SeedGarageAsync();
        var ownerRoleId = await SeedRoleAsync(garageId, "Owner", Scope.Wildcard);
        var actor = PrincipalWithScopes(Scope.StaffManage); // can manage staff, but is not an owner
        var dto = new CreateInvitationDto { Phone = "+15551234", RoleId = ownerRoleId };

        var result = await InvitationEndpoints.CreateInvitationAsync(
            garageId, dto, actor, _tokenService, _invitationService, _invitations);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    // ----- Revoke (garage side) -----

    [Fact]
    public async Task Revoke_removes_the_invitation_and_returns_no_content()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var invitation = await _invitationService.InviteAsync(garageId, "+15551234", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.RevokeInvitationAsync(garageId, invitation.Id, _invitationService);

        Assert.IsType<NoContent>(result);
        Assert.Empty(await _invitations.ListByGarageAsync(garageId));
    }

    [Fact]
    public async Task Revoke_returns_404_when_missing()
    {
        var garageId = await SeedGarageAsync();

        var result = await InvitationEndpoints.RevokeInvitationAsync(garageId, Guid.NewGuid(), _invitationService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    // ----- List my invitations (invitee side) -----

    [Fact]
    public async Task ListMine_returns_invitations_addressed_to_my_phone()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedUserAsync("+15559999");
        await _invitationService.InviteAsync(garageId, "+15559999", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.ListMyInvitationsAsync(
            PrincipalForUser(userId), _tokenService, _users, _invitations);

        var ok = Assert.IsType<Ok<List<MyInvitationDto>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal(garageId, item.GarageId);
        Assert.Equal("Test Garage", item.GarageName);
        Assert.Equal("Mechanic", item.RoleName);
    }

    [Fact]
    public async Task ListMine_returns_empty_when_the_user_has_no_phone()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedUserAsync(phone: null);
        await _invitationService.InviteAsync(garageId, "+15559999", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.ListMyInvitationsAsync(
            PrincipalForUser(userId), _tokenService, _users, _invitations);

        var ok = Assert.IsType<Ok<List<MyInvitationDto>>>(result);
        Assert.Empty(ok.Value!);
    }

    // ----- Accept (invitee side) -----

    [Fact]
    public async Task Accept_returns_no_content_and_creates_the_membership()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedUserAsync("+15559999");
        var invitation = await _invitationService.InviteAsync(garageId, "+15559999", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.AcceptInvitationAsync(
            invitation.Id, PrincipalForUser(userId), _tokenService, _invitationService);

        Assert.IsType<NoContent>(result);
        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        Assert.NotNull(await _memberships.GetAsync(profileId, garageId));
    }

    [Fact]
    public async Task Accept_returns_404_when_the_phone_does_not_match()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedUserAsync("+10000000");
        var invitation = await _invitationService.InviteAsync(garageId, "+15559999", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.AcceptInvitationAsync(
            invitation.Id, PrincipalForUser(userId), _tokenService, _invitationService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task Accept_returns_409_when_already_a_member()
    {
        var garageId = await SeedGarageAsync();
        var roleId = await SeedRoleAsync(garageId, "Mechanic", Scope.StaffRead);
        var userId = await SeedUserAsync("+15559999");
        var profileId = (await _profiles.GetIdByUserIdAsync(userId))!.Value;
        await _memberships.AddAsync(Factories.GarageMembership(profileId, garageId, roleId));
        var invitation = await _invitationService.InviteAsync(garageId, "+15559999", roleId, [Scope.Wildcard]);

        var result = await InvitationEndpoints.AcceptInvitationAsync(
            invitation.Id, PrincipalForUser(userId), _tokenService, _invitationService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }
}
