using MainHub.Api.Authorization;
using MainHub.Api.Repositories;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class RoleRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _sut;

    public RoleRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _garages = new GarageRepository(fixture.DataSource);
        _sut = new RoleRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // CreateAsync + GetByIdAsync proves Map(reader) reads every column back
    // correctly (including the scopes text[]), the single mapping test for RoleEntity.
    [Fact]
    public async Task Create_then_GetById_roundtrips_every_column()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);

        var role = Factories.Role(
            garage.Id,
            name: "Senior Mechanic",
            description: "Leads the workshop",
            scopes: [Scope.StaffRead, Scope.StaffManage, Scope.GarageRead],
            updatedAt: new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        await _sut.CreateAsync(role);
        var fetched = await _sut.GetByIdAsync(role.Id);

        Assert.NotNull(fetched);
        Assert.Equal(role.Id, fetched!.Id);
        Assert.Equal(role.GarageId, fetched.GarageId);
        Assert.Equal(role.Name, fetched.Name);
        Assert.Equal(role.Description, fetched.Description);
        Assert.Equal(role.Scopes, fetched.Scopes);
        Assert.Equal(role.CreatedAt, fetched.CreatedAt);
        Assert.Equal(role.UpdatedAt, fetched.UpdatedAt);
    }

    // The wildcard "*" is stored like any other scope string; the seeded Owner role relies on
    // this round-tripping intact.
    [Fact]
    public async Task Create_persists_wildcard_scope()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var owner = Factories.Role(garage.Id, name: "Owner", scopes: [Scope.Wildcard]);

        await _sut.CreateAsync(owner);
        var fetched = await _sut.GetByIdAsync(owner.Id);

        Assert.NotNull(fetched);
        Assert.Equal(new[] { Scope.Wildcard }, fetched!.Scopes);
    }

    [Fact]
    public async Task Create_persists_null_description_and_updated_at()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id, description: null, updatedAt: null);

        await _sut.CreateAsync(role);
        var fetched = await _sut.GetByIdAsync(role.Id);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.Description);
        Assert.Null(fetched.UpdatedAt);
    }

    [Fact]
    public async Task Create_persists_empty_scopes_as_empty_list()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id, scopes: []);

        await _sut.CreateAsync(role);
        var fetched = await _sut.GetByIdAsync(role.Id);

        Assert.NotNull(fetched);
        Assert.Empty(fetched!.Scopes);
    }

    [Fact]
    public async Task GetById_returns_null_when_missing()
    {
        var missing = await _sut.GetByIdAsync(Guid.NewGuid());
        Assert.Null(missing);
    }
}
