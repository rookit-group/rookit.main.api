using MainHub.Api.DTOs;
using MainHub.Api.Repositories;
using MainHub.Api.Shared;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class VehicleRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly VehicleRepository _sut;
    private readonly UserRepository _users;

    public VehicleRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _sut = new VehicleRepository(fixture.DataSource);
        _users = new UserRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Also exercises enum-as-text round-tripping via WheelDriveType / FuelType /
    // TransmissionType and every nullable column - this is the single Map test.
    [Fact]
    public async Task Create_then_GetById_roundtrips_every_column_including_enums()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);

        var vehicle = Factories.Vehicle(
            userId: user.Id,
            licensePlate: "XY-999-ZZ",
            vin: "5NPE24AF4FH123456",
            brand: "Hyundai",
            model: "Sonata",
            year: 2023,
            boughtAt: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            wheelDriveType: WheelDriveType.AWD,
            engineCapacity: 2000,
            fuelType: FuelType.Hybrid,
            transmissionType: TransmissionType.DualClutch,
            enginePower: 195,
            color: "Blue",
            mileage: 4200,
            photoUrl: "https://example.com/sonata.png",
            updatedAt: new DateTime(2026, 5, 5, 5, 5, 5, DateTimeKind.Utc));

        await _sut.CreateAsync(vehicle);
        var fetched = await _sut.GetByIdAsync(vehicle.Id);

        Assert.NotNull(fetched);
        Assert.Equal(vehicle.Id, fetched!.Id);
        Assert.Equal(user.Id, fetched.UserId);
        Assert.Equal(vehicle.LicensePlate, fetched.LicensePlate);
        Assert.Equal(vehicle.Vin, fetched.Vin);
        Assert.Equal(vehicle.Brand, fetched.Brand);
        Assert.Equal(vehicle.Model, fetched.Model);
        Assert.Equal(vehicle.Year, fetched.Year);
        Assert.Equal(vehicle.BoughtAt, fetched.BoughtAt);
        Assert.Equal(WheelDriveType.AWD, fetched.WheelDriveType);
        Assert.Equal(vehicle.EngineCapacity, fetched.EngineCapacity);
        Assert.Equal(FuelType.Hybrid, fetched.FuelType);
        Assert.Equal(TransmissionType.DualClutch, fetched.TransmissionType);
        Assert.Equal(vehicle.EnginePower, fetched.EnginePower);
        Assert.Equal(vehicle.Color, fetched.Color);
        Assert.Equal(vehicle.Mileage, fetched.Mileage);
        Assert.Equal(vehicle.PhotoUrl, fetched.PhotoUrl);
        Assert.Equal(vehicle.CreatedAt, fetched.CreatedAt);
        Assert.Equal(vehicle.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Create_persists_null_user_id_and_null_photo_url()
    {
        var vehicle = Factories.Vehicle(userId: null, photoUrl: null, updatedAt: null);
        await _sut.CreateAsync(vehicle);
        var fetched = await _sut.GetByIdAsync(vehicle.Id);
        Assert.NotNull(fetched);
        Assert.Null(fetched!.UserId);
        Assert.Null(fetched.PhotoUrl);
        Assert.Null(fetched.UpdatedAt);
    }

    [Fact]
    public async Task GetById_returns_null_when_missing()
    {
        Assert.Null(await _sut.GetByIdAsync(Guid.NewGuid()));
    }

    // ANY(@ids) uuid[] parameter path.
    [Fact]
    public async Task GetByIds_returns_only_matching_ids()
    {
        var a = Factories.Vehicle(licensePlate: "A");
        var b = Factories.Vehicle(licensePlate: "B");
        var c = Factories.Vehicle(licensePlate: "C");
        await _sut.CreateAsync(a);
        await _sut.CreateAsync(b);
        await _sut.CreateAsync(c);

        var result = await _sut.GetByIdsAsync([a.Id, c.Id]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, v => v.Id == a.Id);
        Assert.Contains(result, v => v.Id == c.Id);
    }

    [Fact]
    public async Task GetByIds_empty_list_returns_empty_without_query()
    {
        Assert.Empty(await _sut.GetByIdsAsync([]));
    }

    [Fact]
    public async Task Delete_removes_vehicle()
    {
        var v = Factories.Vehicle();
        await _sut.CreateAsync(v);
        await _sut.DeleteAsync(v.Id);
        Assert.Null(await _sut.GetByIdAsync(v.Id));
    }

    // ORDER BY created_at DESC + pagination.
    [Fact]
    public async Task GetAllVehicles_orders_by_created_at_desc_and_pages()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var v1 = Factories.Vehicle(licensePlate: "v1", createdAt: t0);
        var v2 = Factories.Vehicle(licensePlate: "v2", createdAt: t0.AddHours(1));
        var v3 = Factories.Vehicle(licensePlate: "v3", createdAt: t0.AddHours(2));
        var v4 = Factories.Vehicle(licensePlate: "v4", createdAt: t0.AddHours(3));
        await _sut.CreateAsync(v1);
        await _sut.CreateAsync(v2);
        await _sut.CreateAsync(v3);
        await _sut.CreateAsync(v4);

        var page = await _sut.GetAllVehiclesAsync(skip: 1, limit: 2);

        Assert.Equal(2, page.Count);
        // desc: v4, v3, v2, v1; skip 1 -> v3, v2.
        Assert.Equal(v3.Id, page[0].Id);
        Assert.Equal(v2.Id, page[1].Id);
    }

    [Fact]
    public async Task GetCount_returns_total_rows()
    {
        Assert.Equal(0, await _sut.GetCountAsync());
        await _sut.CreateAsync(Factories.Vehicle());
        await _sut.CreateAsync(Factories.Vehicle());
        Assert.Equal(2, await _sut.GetCountAsync());
    }

    [Fact]
    public async Task GetAllByUser_returns_only_that_users_vehicles()
    {
        var u1 = Factories.User(providerId: "u1");
        var u2 = Factories.User(providerId: "u2");
        await _users.CreateAsync(u1);
        await _users.CreateAsync(u2);

        await _sut.CreateAsync(Factories.Vehicle(userId: u1.Id, licensePlate: "u1a"));
        await _sut.CreateAsync(Factories.Vehicle(userId: u1.Id, licensePlate: "u1b"));
        await _sut.CreateAsync(Factories.Vehicle(userId: u2.Id, licensePlate: "u2a"));
        await _sut.CreateAsync(Factories.Vehicle(userId: null, licensePlate: "orphan"));

        var forU1 = await _sut.GetAllByUserAsync(u1.Id);

        Assert.Equal(2, forU1.Count);
        Assert.All(forU1, v => Assert.Equal(u1.Id, v.UserId));
    }

    [Fact]
    public async Task BelongsToUser_true_when_owned_and_false_otherwise()
    {
        var user = Factories.User();
        var other = Factories.User(providerId: "other");
        await _users.CreateAsync(user);
        await _users.CreateAsync(other);
        var v = Factories.Vehicle(userId: user.Id);
        await _sut.CreateAsync(v);

        Assert.True(await _sut.BelongsToUserAsync(v.Id, user.Id));
        Assert.False(await _sut.BelongsToUserAsync(v.Id, other.Id));
        Assert.False(await _sut.BelongsToUserAsync(Guid.NewGuid(), user.Id));
    }

    [Fact]
    public async Task GetOwnerMap_returns_id_to_userId_for_owned_vehicles()
    {
        var u = Factories.User();
        await _users.CreateAsync(u);
        var owned = Factories.Vehicle(userId: u.Id);
        var orphan = Factories.Vehicle(userId: null);
        await _sut.CreateAsync(owned);
        await _sut.CreateAsync(orphan);

        var map = await _sut.GetOwnerMapAsync([owned.Id, orphan.Id, Guid.NewGuid()]);

        Assert.Single(map);
        Assert.Equal(u.Id, map[owned.Id]);
    }

    [Fact]
    public async Task GetOwnerMap_empty_input_returns_empty()
    {
        var map = await _sut.GetOwnerMapAsync([]);
        Assert.Empty(map);
    }

    [Fact]
    public async Task Attach_sets_user_id()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var v = Factories.Vehicle(userId: null);
        await _sut.CreateAsync(v);

        var attached = await _sut.AttachAsync(v.Id, user.Id);

        Assert.True(attached);
        var fetched = await _sut.GetByIdAsync(v.Id);
        Assert.Equal(user.Id, fetched!.UserId);
    }

    [Fact]
    public async Task Attach_returns_false_when_vehicle_missing()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        Assert.False(await _sut.AttachAsync(Guid.NewGuid(), user.Id));
    }

    [Fact]
    public async Task Detach_nulls_user_id_only_for_matching_owner()
    {
        var owner = Factories.User(providerId: "o");
        var stranger = Factories.User(providerId: "s");
        await _users.CreateAsync(owner);
        await _users.CreateAsync(stranger);
        var v = Factories.Vehicle(userId: owner.Id);
        await _sut.CreateAsync(v);

        // Wrong owner -> false, user_id unchanged.
        Assert.False(await _sut.DetachAsync(v.Id, stranger.Id));
        Assert.Equal(owner.Id, (await _sut.GetByIdAsync(v.Id))!.UserId);

        // Right owner -> true, user_id nulled.
        Assert.True(await _sut.DetachAsync(v.Id, owner.Id));
        Assert.Null((await _sut.GetByIdAsync(v.Id))!.UserId);
    }

    // COALESCE partial update: only supplied fields change, others preserved.
    [Fact]
    public async Task Update_partial_only_touches_supplied_columns()
    {
        var v = Factories.Vehicle(
            licensePlate: "ORIG-1",
            color: "Red",
            mileage: 1000,
            photoUrl: "orig.png",
            boughtAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sut.CreateAsync(v);

        var dto = new UpdateVehicleDto
        {
            LicensePlate = "NEW-1",
            BoughtAt = null,
            Color = null,
            Mileage = 5000,
            PhotoUrl = null,
        };
        var updatedAt = new DateTime(2026, 6, 6, 6, 6, 6, DateTimeKind.Utc);

        var ok = await _sut.UpdateAsync(v.Id, dto, updatedAt);

        Assert.True(ok);
        var fetched = await _sut.GetByIdAsync(v.Id);
        Assert.Equal("NEW-1", fetched!.LicensePlate);
        Assert.Equal(5000, fetched.Mileage);
        Assert.Equal("Red", fetched.Color); // preserved via COALESCE
        Assert.Equal("orig.png", fetched.PhotoUrl); // preserved
        Assert.Equal(v.BoughtAt, fetched.BoughtAt); // preserved
        Assert.Equal(updatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Update_missing_id_returns_false()
    {
        var dto = new UpdateVehicleDto
        {
            LicensePlate = "X",
            BoughtAt = null,
            Color = null,
            Mileage = null,
            PhotoUrl = null,
        };
        Assert.False(await _sut.UpdateAsync(Guid.NewGuid(), dto, DateTime.UtcNow));
    }

    // FK ON DELETE SET NULL: deleting the user nulls the vehicle's user_id.
    [Fact]
    public async Task Deleting_user_nulls_vehicle_user_id_via_cascade()
    {
        var user = Factories.User();
        await _users.CreateAsync(user);
        var v = Factories.Vehicle(userId: user.Id);
        await _sut.CreateAsync(v);

        await _users.DeleteAsync(user.Id);

        var fetched = await _sut.GetByIdAsync(v.Id);
        Assert.NotNull(fetched);
        Assert.Null(fetched!.UserId);
    }
}
