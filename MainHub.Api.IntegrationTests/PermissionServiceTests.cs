using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class PermissionServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _users;
    private readonly InternalUserProfileRepository _profiles;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly GarageMembershipRepository _memberships;
    private readonly PermissionService _sut;

    public PermissionServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _memberships = new GarageMembershipRepository(fixture.DataSource);
        _sut = new PermissionService(_memberships);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ResolveScopes_returns_the_members_scopes_for_the_garage()
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

        var scopes = await _sut.ResolveScopesAsync(user.Id, garage.Id);

        Assert.NotNull(scopes);
        Assert.Equal(new[] { Scope.StaffRead, Scope.RoleManage }, scopes);
    }

    // An owner's wildcard resolves as-is; the request-time Scope.Grants check expands it.
    [Fact]
    public async Task ResolveScopes_returns_wildcard_for_an_owner()
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

        var scopes = await _sut.ResolveScopesAsync(user.Id, garage.Id);

        Assert.Equal(new[] { Scope.Wildcard }, scopes);
    }

    // Non-members resolve to null so the token layer can refuse to mint a garage token.
    [Fact]
    public async Task ResolveScopes_returns_null_when_user_is_not_a_member()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);

        Assert.Null(await _sut.ResolveScopesAsync(user.Id, garage.Id));
    }
}
