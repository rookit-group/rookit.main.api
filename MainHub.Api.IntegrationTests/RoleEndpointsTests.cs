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

// Exercises the role endpoint handlers directly against a real database (there is no HTTP harness in
// this suite). The RequireScope authorization and DTO validation are framework/filter behaviour tested
// elsewhere; here we assert the handlers' service wiring and their exception-to-status-code mapping.
[Collection("Postgres")]
public class RoleEndpointsTests : IAsyncLifetime
{
    private const string SecretKey = "test-secret-key-that-is-at-least-32-bytes-long!!";

    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly TokenService _tokenService;
    private readonly RoleService _roleService;

    public RoleEndpointsTests(PostgresFixture fixture)
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

        _roleService = new RoleService(_roles, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // A garage token carries the caller's resolved scopes in the "scope" claim; the handlers read them
    // back via TokenService.GetScopesFromClaims to enforce the escalation guard.
    private static ClaimsPrincipal PrincipalWithScopes(params string[] scopes) =>
        new(new ClaimsIdentity([new Claim(GarageContext.ScopeClaim, string.Join(' ', scopes))], "test"));

    private static readonly ClaimsPrincipal Owner = PrincipalWithScopes(Scope.Wildcard);

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    [Fact]
    public async Task Create_persists_the_role_and_returns_created()
    {
        var garageId = await SeedGarageAsync();
        var dto = new CreateRoleDto { Name = "Mechanic", Description = "repairs", Scopes = [Scope.GarageRead] };

        var result = await RoleEndpoints.CreateRoleAsync(garageId, dto, Owner, _tokenService, _roleService);

        var created = Assert.IsType<Created<RoleDto>>(result);
        Assert.Equal("Mechanic", created.Value!.Name);
        Assert.False(created.Value.IsSystem);
        Assert.NotNull(await _roles.GetByIdAsync(created.Value.Id));
    }

    [Fact]
    public async Task Create_is_forbidden_when_the_actor_cannot_grant_the_scope()
    {
        var garageId = await SeedGarageAsync();
        var actor = PrincipalWithScopes(Scope.GarageRead); // cannot grant StaffManage
        var dto = new CreateRoleDto { Name = "Escalated", Description = null, Scopes = [Scope.StaffManage] };

        var result = await RoleEndpoints.CreateRoleAsync(garageId, dto, actor, _tokenService, _roleService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task List_returns_the_garages_roles()
    {
        var garageId = await SeedGarageAsync();
        await _roleService.CreateAsync(garageId, "Owner", null, [Scope.Wildcard], [Scope.Wildcard]);
        await _roleService.CreateAsync(garageId, "Mechanic", null, [Scope.GarageRead], [Scope.Wildcard]);

        var result = await RoleEndpoints.ListRolesAsync(garageId, _roleService);

        var ok = Assert.IsType<Ok<List<RoleDto>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Contains(ok.Value, r => r.Name == "Owner");
        Assert.Contains(ok.Value, r => r.Name == "Mechanic");
    }

    [Fact]
    public async Task Update_modifies_the_role_and_returns_ok()
    {
        var garageId = await SeedGarageAsync();
        var role = await _roleService.CreateAsync(garageId, "Mechanic", null, [Scope.GarageRead], [Scope.Wildcard]);
        var dto = new UpdateRoleDto { Name = "Senior Mechanic", Description = "lead", Scopes = [Scope.GarageRead, Scope.StaffRead] };

        var result = await RoleEndpoints.UpdateRoleAsync(garageId, role.Id, dto, Owner, _tokenService, _roleService);

        var ok = Assert.IsType<Ok<RoleDto>>(result);
        Assert.Equal("Senior Mechanic", ok.Value!.Name);
        Assert.Equal([Scope.GarageRead, Scope.StaffRead], ok.Value.Scopes);
    }

    [Fact]
    public async Task Update_returns_not_found_for_a_role_in_another_garage()
    {
        var garageId = await SeedGarageAsync();
        var otherGarageId = await SeedGarageAsync();
        var foreign = await _roleService.CreateAsync(otherGarageId, "Foreign", null, [Scope.GarageRead], [Scope.Wildcard]);
        var dto = new UpdateRoleDto { Name = "Hijacked", Description = null, Scopes = [Scope.GarageRead] };

        var result = await RoleEndpoints.UpdateRoleAsync(garageId, foreign.Id, dto, Owner, _tokenService, _roleService);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Update_returns_conflict_for_a_system_role()
    {
        var garageId = await SeedGarageAsync();
        var owner = Factories.Role(garageId, name: "Owner", scopes: [Scope.Wildcard], isSystem: true);
        await _roles.CreateAsync(owner);
        var dto = new UpdateRoleDto { Name = "Renamed", Description = null, Scopes = [Scope.Wildcard] };

        var result = await RoleEndpoints.UpdateRoleAsync(garageId, owner.Id, dto, Owner, _tokenService, _roleService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_role_and_returns_no_content()
    {
        var garageId = await SeedGarageAsync();
        var role = await _roleService.CreateAsync(garageId, "Temp", null, [Scope.GarageRead], [Scope.Wildcard]);

        var result = await RoleEndpoints.DeleteRoleAsync(garageId, role.Id, _roleService);

        Assert.IsType<NoContent>(result);
        Assert.Null(await _roles.GetByIdAsync(role.Id));
    }

    [Fact]
    public async Task Delete_returns_not_found_when_the_role_is_missing()
    {
        var garageId = await SeedGarageAsync();

        var result = await RoleEndpoints.DeleteRoleAsync(garageId, Guid.NewGuid(), _roleService);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Delete_returns_conflict_when_the_role_is_in_use()
    {
        var garageId = await SeedGarageAsync();
        var role = await _roleService.CreateAsync(garageId, "Mechanic", null, [Scope.GarageRead], [Scope.Wildcard]);

        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        await _memberships.AddAsync(Factories.GarageMembership(profile.Id, garageId, role.Id));

        var result = await RoleEndpoints.DeleteRoleAsync(garageId, role.Id, _roleService);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }
}
