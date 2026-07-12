using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class ServiceHistoryRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly ServiceHistoryRepository _sut;
    private readonly VehicleRepository _vehicles;
    private readonly UserRepository _users;

    public ServiceHistoryRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _sut = new ServiceHistoryRepository(fixture.DataSource);
        _vehicles = new VehicleRepository(fixture.DataSource);
        _users = new UserRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // vehicle_id is a NOT NULL FK on service_histories - every service history
    // in these tests needs a real vehicle row already present.
    private async Task<Guid> CreateVehicleAsync(Guid? userId = null)
    {
        Guid? attachedUser = userId;
        if (attachedUser is null)
        {
            var user = Factories.User();
            await _users.CreateAsync(user);
            attachedUser = user.Id;
        }
        var vehicle = Factories.Vehicle(userId: attachedUser);
        await _vehicles.CreateAsync(vehicle);
        return vehicle.Id;
    }

    // Also proves Map/ReadGroupedAsync reads every column of both tables back
    // correctly.
    [Fact]
    public async Task Create_then_GetById_roundtrips_parent_and_records()
    {
        var vehicleId = await CreateVehicleAsync();
        var shId = Guid.NewGuid();
        var sh = Factories.ServiceHistory(
            vehicleId: vehicleId,
            id: shId,
            title: "60k service",
            description: "Full inspection",
            updatedAt: new DateTime(2026, 3, 3, 3, 3, 3, DateTimeKind.Utc),
            records:
            [
                Factories.Record(serviceHistoryId: shId, title: "Oil", description: "5W-30", price: 40),
                Factories.Record(serviceHistoryId: shId, title: "Filter", description: "Cabin", price: 25),
            ]);

        await _sut.CreateAsync(sh);
        var fetched = await _sut.GetByIdAsync(vehicleId, shId);

        Assert.NotNull(fetched);
        Assert.Equal(sh.Id, fetched!.Id);
        Assert.Equal(vehicleId, fetched.VehicleId);
        Assert.Equal(sh.Title, fetched.Title);
        Assert.Equal(sh.Description, fetched.Description);
        Assert.Equal(sh.CreatedAt, fetched.CreatedAt);
        Assert.Equal(sh.UpdatedAt, fetched.UpdatedAt);
        Assert.Equal(2, fetched.Records.Count);
        var byTitle = fetched.Records.ToDictionary(r => r.Title);
        Assert.Equal(40, byTitle["Oil"].Price);
        Assert.Equal("5W-30", byTitle["Oil"].Description);
        Assert.Equal(shId, byTitle["Oil"].ServiceHistoryId);
    }

    // A service history with no records must still come back (LEFT JOIN).
    [Fact]
    public async Task GetById_returns_parent_with_empty_records()
    {
        var vehicleId = await CreateVehicleAsync();
        var sh = Factories.ServiceHistory(vehicleId, records: []);
        await _sut.CreateAsync(sh);

        var fetched = await _sut.GetByIdAsync(vehicleId, sh.Id);

        Assert.NotNull(fetched);
        Assert.Empty(fetched!.Records);
    }

    [Fact]
    public async Task GetById_returns_null_when_vehicle_id_does_not_match()
    {
        var vehicleId = await CreateVehicleAsync();
        var otherVehicleId = await CreateVehicleAsync();
        var sh = Factories.ServiceHistory(vehicleId);
        await _sut.CreateAsync(sh);

        Assert.Null(await _sut.GetByIdAsync(otherVehicleId, sh.Id));
    }

    [Fact]
    public async Task GetByServiceHistoryId_finds_ignoring_vehicle()
    {
        var vehicleId = await CreateVehicleAsync();
        var sh = Factories.ServiceHistory(vehicleId);
        await _sut.CreateAsync(sh);

        var fetched = await _sut.GetByServiceHistoryIdAsync(sh.Id);
        Assert.NotNull(fetched);
        Assert.Equal(sh.Id, fetched!.Id);
    }

    [Fact]
    public async Task GetByServiceHistoryId_returns_null_when_missing()
    {
        Assert.Null(await _sut.GetByServiceHistoryIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllByVehicleId_returns_multiple_parents_each_with_own_records()
    {
        var vehicleId = await CreateVehicleAsync();
        var sh1Id = Guid.NewGuid();
        var sh2Id = Guid.NewGuid();
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId, id: sh1Id,
            records:
            [
                Factories.Record(serviceHistoryId: sh1Id, title: "A", price: 10),
            ]));
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId, id: sh2Id,
            records:
            [
                Factories.Record(serviceHistoryId: sh2Id, title: "B", price: 20),
                Factories.Record(serviceHistoryId: sh2Id, title: "C", price: 30),
            ]));

        var all = await _sut.GetAllByVehicleIdAsync(vehicleId);

        Assert.Equal(2, all.Count);
        var byId = all.ToDictionary(sh => sh.Id);
        Assert.Single(byId[sh1Id].Records);
        Assert.Equal(2, byId[sh2Id].Records.Count);
    }

    [Fact]
    public async Task GetAllByVehicleId_returns_empty_when_none()
    {
        var vehicleId = await CreateVehicleAsync();
        Assert.Empty(await _sut.GetAllByVehicleIdAsync(vehicleId));
    }

    // UpdateAsync replaces records: delete-all-and-reinsert semantics inside
    // one transaction.
    [Fact]
    public async Task Update_replaces_records_and_updates_parent()
    {
        var vehicleId = await CreateVehicleAsync();
        var shId = Guid.NewGuid();
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId, id: shId, title: "Old",
            records:
            [
                Factories.Record(serviceHistoryId: shId, title: "Old-A"),
                Factories.Record(serviceHistoryId: shId, title: "Old-B"),
            ]));

        var updated = Factories.ServiceHistory(vehicleId, id: shId, title: "New", description: "Rewritten",
            updatedAt: new DateTime(2026, 7, 7, 7, 7, 7, DateTimeKind.Utc),
            records:
            [
                Factories.Record(serviceHistoryId: shId, title: "New-A", price: 111),
            ]);
        var ok = await _sut.UpdateAsync(updated);

        Assert.True(ok);
        var fetched = await _sut.GetByServiceHistoryIdAsync(shId);
        Assert.Equal("New", fetched!.Title);
        Assert.Equal("Rewritten", fetched.Description);
        Assert.Equal(updated.UpdatedAt, fetched.UpdatedAt);
        Assert.Single(fetched.Records);
        Assert.Equal("New-A", fetched.Records[0].Title);
        Assert.Equal(111, fetched.Records[0].Price);
    }

    // Missing parent must roll back the transaction and return false without
    // touching anything.
    [Fact]
    public async Task Update_missing_parent_returns_false_and_rolls_back()
    {
        var vehicleId = await CreateVehicleAsync();
        var ghost = Factories.ServiceHistory(vehicleId, records:
        [
            Factories.Record(title: "Should not persist"),
        ]);

        var ok = await _sut.UpdateAsync(ghost);

        Assert.False(ok);
        Assert.Empty(await _sut.GetAllByVehicleIdAsync(vehicleId));
    }

    // CASCADE on service_history_records fires from service_histories delete.
    [Fact]
    public async Task Delete_removes_parent_and_records()
    {
        var vehicleId = await CreateVehicleAsync();
        var shId = Guid.NewGuid();
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId, id: shId, records:
        [
            Factories.Record(serviceHistoryId: shId),
            Factories.Record(serviceHistoryId: shId),
        ]));

        var ok = await _sut.DeleteAsync(shId);

        Assert.True(ok);
        Assert.Null(await _sut.GetByServiceHistoryIdAsync(shId));
        Assert.Equal(0, await CountRecordsAsync(shId));
    }

    [Fact]
    public async Task Delete_missing_id_returns_false()
    {
        Assert.False(await _sut.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteAllByVehicleId_removes_every_history_for_vehicle()
    {
        var vehicleId = await CreateVehicleAsync();
        var otherVehicleId = await CreateVehicleAsync();
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId));
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId));
        await _sut.CreateAsync(Factories.ServiceHistory(otherVehicleId));

        await _sut.DeleteAllByVehicleIdAsync(vehicleId);

        Assert.Empty(await _sut.GetAllByVehicleIdAsync(vehicleId));
        Assert.Single(await _sut.GetAllByVehicleIdAsync(otherVehicleId));
    }

    // Deleting a vehicle should CASCADE-delete its service histories AND their records.
    [Fact]
    public async Task Deleting_vehicle_cascades_to_histories_and_records()
    {
        var vehicleId = await CreateVehicleAsync();
        var shId = Guid.NewGuid();
        await _sut.CreateAsync(Factories.ServiceHistory(vehicleId, id: shId, records:
        [
            Factories.Record(serviceHistoryId: shId),
        ]));

        await _vehicles.DeleteAsync(vehicleId);

        Assert.Null(await _sut.GetByServiceHistoryIdAsync(shId));
        Assert.Equal(0, await CountRecordsAsync(shId));
    }

    private async Task<long> CountRecordsAsync(Guid serviceHistoryId)
    {
        await using var cmd = _fixture.DataSource.CreateCommand(
            "SELECT COUNT(*) FROM service_history_records WHERE service_history_id = @id");
        cmd.Parameters.AddWithValue("id", serviceHistoryId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
