using MainHub.Api.Authorization;
using MainHub.Api.Endpoints;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Exercises the role listing endpoint handler directly against a real database (there is no HTTP
// harness in this suite). The RequireScope authorization is framework/filter behaviour tested
// elsewhere; here we assert the handler's service wiring and DTO mapping.
[Collection("Postgres")]
public class RoleEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly RoleService _roleService;

    public RoleEndpointsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _roleService = new RoleService(_roles);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedGarageAsync()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        return garage.Id;
    }

    [Fact]
    public async Task List_returns_the_garages_roles()
    {
        var garageId = await SeedGarageAsync();
        await _roles.CreateAsync(Factories.Role(garageId, name: "Owner", scopes: [Scope.Wildcard], isSystem: true));
        await _roles.CreateAsync(Factories.Role(garageId, name: "Mechanic", scopes: [Scope.GarageRead]));

        var result = await RoleEndpoints.ListRolesAsync(garageId, _roleService);

        var ok = Assert.IsType<Ok<List<RoleDto>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Contains(ok.Value, r => r.Name == "Owner");
        Assert.Contains(ok.Value, r => r.Name == "Mechanic");
    }
}
