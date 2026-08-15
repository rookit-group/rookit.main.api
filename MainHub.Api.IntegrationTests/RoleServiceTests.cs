using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class RoleServiceTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly RoleService _sut;

    public RoleServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _sut = new RoleService(_roles);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    // ----- CreateMany -----

    // The bulk seed used during garage creation persists every supplied role.
    [Fact]
    public async Task CreateMany_persists_all_roles()
    {
        var garageId = await SeedGarageAsync();
        var owner = Factories.Role(garageId, name: "Owner", scopes: [Scope.Wildcard], isSystem: true);
        var mechanic = Factories.Role(garageId, name: "Mechanic", scopes: [Scope.GarageRead]);

        await _sut.CreateManyAsync([owner, mechanic]);

        var roles = await _roles.ListByGarageAsync(garageId);
        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.Name == "Owner");
        Assert.Contains(roles, r => r.Name == "Mechanic");
    }

    // ----- List -----

    [Fact]
    public async Task ListByGarage_returns_the_garages_roles()
    {
        var garageId = await SeedGarageAsync();
        await _roles.CreateAsync(Factories.Role(garageId, name: "Owner", scopes: [Scope.Wildcard], isSystem: true));
        await _roles.CreateAsync(Factories.Role(garageId, name: "Mechanic", scopes: [Scope.GarageRead]));

        var roles = await _sut.ListByGarageAsync(garageId);

        Assert.Equal(2, roles.Count);
        Assert.Contains(roles, r => r.Name == "Owner");
        Assert.Contains(roles, r => r.Name == "Mechanic");
    }
}
