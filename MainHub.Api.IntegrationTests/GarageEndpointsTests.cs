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

// Exercises the garage-session endpoint handler directly (there is no HTTP harness in this suite):
// membership is resolved against a real database, and the handler either mints a garage token or
// refuses. The RequireInternalIdentityJwt policy (401 for anonymous callers) is framework behaviour
// and is not re-tested here.
[Collection("Postgres")]
public class GarageEndpointsTests : IAsyncLifetime
{
    private const string SecretKey = "test-secret-key-that-is-at-least-32-bytes-long!!";

    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly TokenService _tokenService;
    private readonly PermissionService _permissionService;
    private readonly GarageService _garageService;
    private readonly IOptions<GarageJwtSettings> _garageOptions;

    public GarageEndpointsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);

        _garageOptions = Options.Create(new GarageJwtSettings
        {
            SecretKey = SecretKey,
            Issuer = "garage-issuer",
            Audience = "garage-aud",
            ExpirationMinutes = 5,
        });

        _tokenService = new TokenService(
            Options.Create(new JwtSettings { SecretKey = SecretKey, Issuer = "m", Audience = "m" }),
            Options.Create(new AdminJwtSettings { SecretKey = SecretKey, Issuer = "a", Audience = "a" }),
            Options.Create(new InternalIdentityJwtSettings { SecretKey = SecretKey, Issuer = "i", Audience = "i" }),
            _garageOptions);

        _permissionService = new PermissionService(_memberships);
        var roleService = new RoleService(_roles, _memberships);
        _garageService = new GarageService(fixture.DataSource, _garages, roleService, _memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static ClaimsPrincipal PrincipalFor(Guid userId) =>
        new(new ClaimsIdentity([new Claim("userId", userId.ToString())], "test"));

    private Task<IResult> InvokeAsync(Guid userId, Guid garageId) =>
        GarageEndpoints.CreateGarageSessionAsync(garageId, PrincipalFor(userId), _tokenService, _permissionService, _garageOptions);

    [Fact]
    public async Task Session_mints_a_garage_token_carrying_the_members_scopes()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id, scopes: [Scope.StaffRead, Scope.RoleManage]);
        await _roles.CreateAsync(role);
        await _memberships.AddAsync(Factories.GarageMembership(profile.Id, garage.Id, role.Id));

        var result = await InvokeAsync(user.Id, garage.Id);

        var ok = Assert.IsType<Ok<string>>(result);

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(ok.Value);
        Assert.Equal(garage.Id.ToString(), jwt.Claims.Single(c => c.Type == "garage_id").Value);
        Assert.Equal($"{Scope.StaffRead} {Scope.RoleManage}", jwt.Claims.Single(c => c.Type == "scope").Value);
    }

    [Fact]
    public async Task Session_mints_a_wildcard_token_for_an_owner()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var owner = Factories.Role(garage.Id, name: "Owner", scopes: [Scope.Wildcard], isSystem: true);
        await _roles.CreateAsync(owner);
        await _memberships.AddAsync(Factories.GarageMembership(profile.Id, garage.Id, owner.Id));

        var result = await InvokeAsync(user.Id, garage.Id);

        var ok = Assert.IsType<Ok<string>>(result);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(ok.Value);
        Assert.Equal(Scope.Wildcard, jwt.Claims.Single(c => c.Type == "scope").Value);
    }

    [Fact]
    public async Task Session_is_forbidden_when_the_user_is_not_a_member()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);

        var result = await InvokeAsync(user.Id, garage.Id);

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task ListMyGarages_returns_the_callers_garages_with_role_names()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        var garage = Factories.Garage(name: "Alpha Auto");
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id, name: "Mechanic");
        await _roles.CreateAsync(role);
        await _memberships.AddAsync(Factories.GarageMembership(profile.Id, garage.Id, role.Id));

        var result = await GarageEndpoints.ListMyGaragesAsync(PrincipalFor(user.Id), _tokenService, _memberships);

        var ok = Assert.IsType<Ok<List<GarageListItemDto>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal(garage.Id, item.GarageId);
        Assert.Equal("Alpha Auto", item.Name);
        Assert.Equal("Mechanic", item.RoleName);
    }

    [Fact]
    public async Task ListMyGarages_returns_empty_for_a_user_with_no_memberships()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);

        var result = await GarageEndpoints.ListMyGaragesAsync(PrincipalFor(user.Id), _tokenService, _memberships);

        var ok = Assert.IsType<Ok<List<GarageListItemDto>>>(result);
        Assert.Empty(ok.Value!);
    }

    // Self-serve creation: a logged-in user with no garages creates one and is atomically seeded as
    // its owner. The profile is created on demand (EnsureAsync), and the new garage immediately shows
    // up in the caller's list with the "Owner" role.
    [Fact]
    public async Task CreateGarage_creates_the_garage_and_makes_the_caller_its_owner()
    {
        var user = Factories.User();
        await _users.CreateAsync(user); // no profile yet - the handler must EnsureAsync it
        var dto = new CreateGarageDto { Name = "Downtown Motors" };

        var result = await GarageEndpoints.CreateGarageAsync(
            dto, PrincipalFor(user.Id), _tokenService, _profiles, _garageService);

        var created = Assert.IsType<Created<GarageListItemDto>>(result);
        Assert.Equal("Downtown Motors", created.Value!.Name);
        Assert.Equal("Owner", created.Value.RoleName);

        // The caller can now see the garage in their own list as its owner.
        var list = await GarageEndpoints.ListMyGaragesAsync(PrincipalFor(user.Id), _tokenService, _memberships);
        var ok = Assert.IsType<Ok<List<GarageListItemDto>>>(list);
        var item = Assert.Single(ok.Value!);
        Assert.Equal(created.Value.GarageId, item.GarageId);
        Assert.Equal("Owner", item.RoleName);
    }

    // The seeded Owner role holds the wildcard, so opening a session for the just-created garage
    // yields a token carrying "*" - proving the self-serve owner really has full access.
    [Fact]
    public async Task CreateGarage_owner_can_open_a_wildcard_session_for_the_new_garage()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);

        var created = Assert.IsType<Created<GarageListItemDto>>(await GarageEndpoints.CreateGarageAsync(
            new CreateGarageDto { Name = "Grant Test Garage" },
            PrincipalFor(user.Id), _tokenService, _profiles, _garageService));

        var session = await InvokeAsync(user.Id, created.Value!.GarageId);

        var ok = Assert.IsType<Ok<string>>(session);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(ok.Value);
        Assert.Equal(Scope.Wildcard, jwt.Claims.Single(c => c.Type == "scope").Value);
    }
}
