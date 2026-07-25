using System.Security.Claims;
using MainHub.Api.Config;
using MainHub.Api.Endpoints;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Exercises the /api/me handler directly against a real database (there is no HTTP harness in this
// suite). The RequireInternalIdentityJwt policy (401 for anonymous callers) is framework behaviour
// and is not re-tested here; this asserts the handler projects the caller's users row into the DTO.
[Collection("Postgres")]
public class MeEndpointsTests : IAsyncLifetime
{
    private const string SecretKey = "test-secret-key-that-is-at-least-32-bytes-long!!";

    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly TokenService _tokenService;
    private readonly UserService _userService;

    public MeEndpointsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);

        _tokenService = new TokenService(
            Options.Create(new JwtSettings { SecretKey = SecretKey, Issuer = "m", Audience = "m" }),
            Options.Create(new AdminJwtSettings { SecretKey = SecretKey, Issuer = "a", Audience = "a" }),
            Options.Create(new InternalIdentityJwtSettings { SecretKey = SecretKey, Issuer = "i", Audience = "i" }),
            Options.Create(new GarageJwtSettings { SecretKey = SecretKey, Issuer = "g", Audience = "g" }));

        _userService = new UserService(
            _users,
            new ExternalUserProfileRepository(fixture.DataSource),
            new VehicleRepository(fixture.DataSource));
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static ClaimsPrincipal PrincipalFor(Guid userId) =>
        new(new ClaimsIdentity([new Claim("userId", userId.ToString())], "test"));

    [Fact]
    public async Task GetCurrentUser_returns_the_callers_id_and_basic_info()
    {
        var user = Factories.User(
            name: "Grace", email: "grace@example.com", pictureUrl: "https://example.com/grace.png");
        await _users.CreateAsync(user);

        var result = await MeEndpoints.GetCurrentUserAsync(PrincipalFor(user.Id), _tokenService, _userService);

        var ok = Assert.IsType<Ok<CurrentUserDto>>(result);
        Assert.Equal(user.Id, ok.Value!.UserId);
        Assert.Equal("Grace", ok.Value.Name);
        Assert.Equal("grace@example.com", ok.Value.Email);
        Assert.Equal("https://example.com/grace.png", ok.Value.PictureUrl);
    }

    [Fact]
    public async Task GetCurrentUser_tolerates_null_optional_identity_fields()
    {
        var user = Factories.User(name: "Nadia", email: null, pictureUrl: null);
        await _users.CreateAsync(user);

        var result = await MeEndpoints.GetCurrentUserAsync(PrincipalFor(user.Id), _tokenService, _userService);

        var ok = Assert.IsType<Ok<CurrentUserDto>>(result);
        Assert.Equal("Nadia", ok.Value!.Name);
        Assert.Null(ok.Value.Email);
        Assert.Null(ok.Value.PictureUrl);
    }

    [Fact]
    public async Task GetCurrentUser_returns_not_found_when_the_user_no_longer_exists()
    {
        var result = await MeEndpoints.GetCurrentUserAsync(PrincipalFor(Guid.NewGuid()), _tokenService, _userService);

        Assert.IsType<NotFound>(result);
    }
}
