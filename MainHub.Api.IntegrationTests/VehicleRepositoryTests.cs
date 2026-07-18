using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;
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
    private readonly InternalUserProfileRepository _profiles;

    public VehicleRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _sut = new VehicleRepository(fixture.DataSource);
        _users = new UserRepository(fixture.DataSource);
        _profiles = new InternalUserProfileRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // vehicles.internal_user_profile_id is NOT NULL and FKs against
    // internal_user_profiles, so every vehicle in these tests needs a user +
    // profile pair already inserted.
    private async Task<(Guid userId, Guid profileId)> CreateUserAndProfileAsync(string? providerId = null)
    {
        var user = Factories.User(providerId: providerId);
        await _users.CreateAsync(user);
        var profile = Factories.InternalUserProfile(user.Id);
        await _profiles.CreateAsync(profile);
        return (user.Id, profile.Id);
    }

    // Also exercises enum-as-text round-tripping via WheelDriveType / FuelType /
    // TransmissionType and every nullable column - this is the single Map test.
    [Fact]
    public async Task Create_then_GetById_roundtrips_every_column_including_enums()
    {
        var (_, profileId) = await CreateUserAndProfileAsync();

        var vehicle = Factories.Vehicle(
            internalUserProfileId: profileId,
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
            photoStorageKeys: ["vehicles/photos/test/sonata"],
            updatedAt: new DateTime(2026, 5, 5, 5, 5, 5, DateTimeKind.Utc));

        await _sut.CreateAsync(vehicle);
        var fetched = await _sut.GetByIdAsync(vehicle.Id);

        Assert.NotNull(fetched);
        Assert.Equal(vehicle.Id, fetched!.Id);
        Assert.Equal(profileId, fetched.InternalUserProfileId);
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
        Assert.Equal(vehicle.PhotoStorageKeys, fetched.PhotoStorageKeys);
        Assert.Equal(vehicle.CreatedAt, fetched.CreatedAt);
        Assert.Equal(vehicle.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Create_persists_null_photo_storage_keys_and_null_updated_at()
    {
        var (_, profileId) = await CreateUserAndProfileAsync();
        var vehicle = Factories.Vehicle(internalUserProfileId: profileId, photoStorageKeys: null, updatedAt: null);
        await _sut.CreateAsync(vehicle);
        var fetched = await _sut.GetByIdAsync(vehicle.Id);
        Assert.NotNull(fetched);
        Assert.Null(fetched!.PhotoStorageKeys);
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
        var (_, profileId) = await CreateUserAndProfileAsync();
        var a = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "A");
        var b = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "B");
        var c = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "C");
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
        var (_, profileId) = await CreateUserAndProfileAsync();
        var v = Factories.Vehicle(internalUserProfileId: profileId);
        await _sut.CreateAsync(v);
        await _sut.DeleteAsync(v.Id);
        Assert.Null(await _sut.GetByIdAsync(v.Id));
    }

    // ORDER BY created_at DESC + pagination.
    [Fact]
    public async Task GetAllVehicles_orders_by_created_at_desc_and_pages()
    {
        var (_, profileId) = await CreateUserAndProfileAsync();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var v1 = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "v1", createdAt: t0);
        var v2 = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "v2", createdAt: t0.AddHours(1));
        var v3 = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "v3", createdAt: t0.AddHours(2));
        var v4 = Factories.Vehicle(internalUserProfileId: profileId, licensePlate: "v4", createdAt: t0.AddHours(3));
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
        var (_, profileId) = await CreateUserAndProfileAsync();
        Assert.Equal(0, await _sut.GetCountAsync());
        await _sut.CreateAsync(Factories.Vehicle(internalUserProfileId: profileId));
        await _sut.CreateAsync(Factories.Vehicle(internalUserProfileId: profileId));
        Assert.Equal(2, await _sut.GetCountAsync());
    }

    [Fact]
    public async Task GetAllByUser_returns_only_that_users_vehicles()
    {
        var (u1Id, p1) = await CreateUserAndProfileAsync(providerId: "u1");
        var (_, p2) = await CreateUserAndProfileAsync(providerId: "u2");

        await _sut.CreateAsync(Factories.Vehicle(internalUserProfileId: p1, licensePlate: "u1a"));
        await _sut.CreateAsync(Factories.Vehicle(internalUserProfileId: p1, licensePlate: "u1b"));
        await _sut.CreateAsync(Factories.Vehicle(internalUserProfileId: p2, licensePlate: "u2a"));

        var forU1 = await _sut.GetAllByUserAsync(u1Id);

        Assert.Equal(2, forU1.Count);
        Assert.All(forU1, v => Assert.Equal(p1, v.InternalUserProfileId));
    }

    [Fact]
    public async Task BelongsToUser_true_when_owned_and_false_otherwise()
    {
        var (userId, profileId) = await CreateUserAndProfileAsync();
        var (otherId, _) = await CreateUserAndProfileAsync(providerId: "other");
        var v = Factories.Vehicle(internalUserProfileId: profileId);
        await _sut.CreateAsync(v);

        Assert.True(await _sut.BelongsToUserAsync(v.Id, userId));
        Assert.False(await _sut.BelongsToUserAsync(v.Id, otherId));
        Assert.False(await _sut.BelongsToUserAsync(Guid.NewGuid(), userId));
    }

    [Fact]
    public async Task GetOwnerMap_returns_id_to_userId_for_owned_vehicles()
    {
        var (userId, profileId) = await CreateUserAndProfileAsync();
        var owned = Factories.Vehicle(internalUserProfileId: profileId);
        await _sut.CreateAsync(owned);

        var map = await _sut.GetOwnerMapAsync([owned.Id, Guid.NewGuid()]);

        Assert.Single(map);
        Assert.Equal(userId, map[owned.Id]);
    }

    [Fact]
    public async Task GetOwnerMap_empty_input_returns_empty()
    {
        var map = await _sut.GetOwnerMapAsync([]);
        Assert.Empty(map);
    }

    // COALESCE partial update: only supplied fields change, others preserved.
    [Fact]
    public async Task Update_partial_only_touches_supplied_columns()
    {
        var (_, profileId) = await CreateUserAndProfileAsync();
        var v = Factories.Vehicle(
            internalUserProfileId: profileId,
            licensePlate: "ORIG-1",
            color: "Red",
            mileage: 1000,
            photoStorageKeys: ["orig-key"],
            boughtAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await _sut.CreateAsync(v);

        var dto = new UpdateVehicleDto
        {
            LicensePlate = "NEW-1",
            BoughtAt = null,
            Color = null,
            Mileage = 5000,
            PhotoStorageKeys = null,
        };
        var updatedAt = new DateTime(2026, 6, 6, 6, 6, 6, DateTimeKind.Utc);

        var ok = await _sut.UpdateAsync(v.Id, dto, updatedAt);

        Assert.True(ok);
        var fetched = await _sut.GetByIdAsync(v.Id);
        Assert.Equal("NEW-1", fetched!.LicensePlate);
        Assert.Equal(5000, fetched.Mileage);
        Assert.Equal("Red", fetched.Color); // preserved via COALESCE
        Assert.Equal(new List<string> { "orig-key" }, fetched.PhotoStorageKeys); // preserved
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
            PhotoStorageKeys = null,
        };
        Assert.False(await _sut.UpdateAsync(Guid.NewGuid(), dto, DateTime.UtcNow));
    }

    // FK ON DELETE CASCADE now: deleting the user removes their profile which
    // in turn CASCADEs to their vehicles.
    [Fact]
    public async Task Deleting_user_cascades_vehicle_deletion()
    {
        var (userId, profileId) = await CreateUserAndProfileAsync();
        var v = Factories.Vehicle(internalUserProfileId: profileId);
        await _sut.CreateAsync(v);

        await _users.DeleteAsync(userId);

        Assert.Null(await _sut.GetByIdAsync(v.Id));
    }
}
