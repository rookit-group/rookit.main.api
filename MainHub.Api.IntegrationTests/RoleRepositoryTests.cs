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
            isSystem: true,
            updatedAt: new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        await _sut.CreateAsync(role);
        var fetched = await _sut.GetByIdAsync(role.Id);

        Assert.NotNull(fetched);
        Assert.Equal(role.Id, fetched!.Id);
        Assert.Equal(role.GarageId, fetched.GarageId);
        Assert.Equal(role.Name, fetched.Name);
        Assert.Equal(role.Description, fetched.Description);
        Assert.Equal(role.Scopes, fetched.Scopes);
        Assert.True(fetched.IsSystem);
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

    [Fact]
    public async Task ListByGarage_returns_only_that_garages_roles_ordered_by_created_at()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var other = Factories.Garage();
        await _garages.CreateAsync(other);

        var first = Factories.Role(garage.Id, name: "Owner", createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = Factories.Role(garage.Id, name: "Mechanic", createdAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var elsewhere = Factories.Role(other.Id, name: "Other Garage Role");
        await _sut.CreateAsync(first);
        await _sut.CreateAsync(second);
        await _sut.CreateAsync(elsewhere);

        var roles = await _sut.ListByGarageAsync(garage.Id);

        Assert.Equal(new[] { first.Id, second.Id }, roles.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task ListByGarage_returns_empty_when_garage_has_no_roles()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);

        var roles = await _sut.ListByGarageAsync(garage.Id);

        Assert.Empty(roles);
    }

    [Fact]
    public async Task Update_changes_mutable_fields_and_returns_true()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id, name: "Mechanic", description: "old", scopes: [Scope.GarageRead]);
        await _sut.CreateAsync(role);

        role.Name = "Senior Mechanic";
        role.Description = "new";
        role.Scopes = [Scope.GarageRead, Scope.StaffManage];
        role.UpdatedAt = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        var updated = await _sut.UpdateAsync(role);
        var fetched = await _sut.GetByIdAsync(role.Id);

        Assert.True(updated);
        Assert.NotNull(fetched);
        Assert.Equal("Senior Mechanic", fetched!.Name);
        Assert.Equal("new", fetched.Description);
        Assert.Equal(new[] { Scope.GarageRead, Scope.StaffManage }, fetched.Scopes);
        Assert.Equal(role.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Update_returns_false_when_role_missing()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var ghost = Factories.Role(garage.Id); // never inserted

        var updated = await _sut.UpdateAsync(ghost);

        Assert.False(updated);
    }

    [Fact]
    public async Task Delete_removes_role_and_returns_true()
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id);
        await _sut.CreateAsync(role);

        var deleted = await _sut.DeleteAsync(role.Id);

        Assert.True(deleted);
        Assert.Null(await _sut.GetByIdAsync(role.Id));
    }

    [Fact]
    public async Task Delete_returns_false_when_role_missing()
    {
        var deleted = await _sut.DeleteAsync(Guid.NewGuid());
        Assert.False(deleted);
    }
}
