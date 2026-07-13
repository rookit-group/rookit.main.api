using MainHub.Api.Data;
using Shared.Contracts.DTOs;
using Shared.Contracts.Enums;
using MainHub.Api.Models;
using NpgsqlTypes;
using Npgsql;

namespace MainHub.Api.Repositories;

public interface IVehicleRepository
{
    Task CreateAsync(VehicleEntity vehicle);
    Task<VehicleEntity?> GetByIdAsync(Guid id);
    Task<List<VehicleEntity>> GetByIdsAsync(List<Guid> ids);
    Task DeleteAsync(Guid vehicleId);
    Task<List<VehicleEntity>> GetAllVehiclesAsync(int skip, int limit);
    Task<long> GetCountAsync();
    Task<List<VehicleEntity>> GetAllByUserAsync(Guid userId);
    Task<bool> BelongsToUserAsync(Guid vehicleId, Guid userId);
    Task<Dictionary<Guid, Guid>> GetOwnerMapAsync(IReadOnlyCollection<Guid> vehicleIds);
    Task<bool> AttachAsync(Guid vehicleId, Guid userId);
    Task<bool> DetachAsync(Guid vehicleId, Guid userId);
    Task<bool> UpdateAsync(Guid id, UpdateVehicleDto dto, DateTime updatedAt);
}

public class VehicleRepository : IVehicleRepository
{
    private const string SelectColumns =
        "id, user_id, license_plate, vin, brand, model, year_created, bought_at, " +
        "wheel_drive_type, engine_capacity, fuel_type, transmission_type, engine_power, " +
        "color, mileage, photo_storage_keys, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public VehicleRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // Mongo equivalent: _collection.InsertOneAsync(vehicle). user_id is the FK
    // that replaces the old UserEntity.VehicleIds array - ownership now lives
    // here on the "many" side instead of as an id list on the user document.
    // Enum properties (WheelDriveType/FuelType/TransmissionType) are written
    // with .ToString() since the column is plain text, not a native PG enum.
    public async Task CreateAsync(VehicleEntity vehicle)
    {
        const string sql = @"
            INSERT INTO vehicles (
                id, user_id, license_plate, vin, brand, model, year_created, bought_at,
                wheel_drive_type, engine_capacity, fuel_type, transmission_type, engine_power,
                color, mileage, photo_storage_keys, created_at, updated_at
            ) VALUES (
                @id, @user_id, @license_plate, @vin, @brand, @model, @year_created, @bought_at,
                @wheel_drive_type, @engine_capacity, @fuel_type, @transmission_type, @engine_power,
                @color, @mileage, @photo_storage_keys, @created_at, @updated_at
            )";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", vehicle.Id);
        cmd.Parameters.AddWithValue("user_id", NpgsqlReaderExtensions.NullableParam(vehicle.UserId));
        cmd.Parameters.AddWithValue("license_plate", vehicle.LicensePlate);
        cmd.Parameters.AddWithValue("vin", vehicle.Vin);
        cmd.Parameters.AddWithValue("brand", vehicle.Brand);
        cmd.Parameters.AddWithValue("model", vehicle.Model);
        cmd.Parameters.AddWithValue("year_created", vehicle.Year);
        cmd.Parameters.AddWithValue("bought_at", NpgsqlReaderExtensions.NullableParam(vehicle.BoughtAt));
        cmd.Parameters.AddWithValue("wheel_drive_type", vehicle.WheelDriveType.ToString());
        cmd.Parameters.AddWithValue("engine_capacity", vehicle.EngineCapacity);
        cmd.Parameters.AddWithValue("fuel_type", vehicle.FuelType.ToString());
        cmd.Parameters.AddWithValue("transmission_type", vehicle.TransmissionType.ToString());
        cmd.Parameters.AddWithValue("engine_power", vehicle.EnginePower);
        cmd.Parameters.AddWithValue("color", vehicle.Color);
        cmd.Parameters.AddWithValue("mileage", vehicle.Mileage);
        cmd.Parameters.Add(new NpgsqlParameter("photo_storage_keys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = (object?)vehicle.PhotoStorageKeys?.ToArray() ?? DBNull.Value });
        cmd.Parameters.AddWithValue("created_at", vehicle.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(vehicle.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<VehicleEntity?> GetByIdAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM vehicles WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    // Mongo equivalent: .Find(Builders<T>.Filter.In(v => v.Id, ids)) - the
    // "$in" operator. Postgres has no array-membership filter operator, so we
    // pass a real C# array as a single parameter typed as an array of uuid
    // (NpgsqlDbType.Array | NpgsqlDbType.Uuid) and match with ANY(@ids).
    public async Task<List<VehicleEntity>> GetByIdsAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return [];
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM vehicles WHERE id = ANY(@ids)");
        cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
        return await ReadListAsync(cmd);
    }

    public async Task DeleteAsync(Guid vehicleId)
    {
        await using var cmd = _dataSource.CreateCommand("DELETE FROM vehicles WHERE id = @id");
        cmd.Parameters.AddWithValue("id", vehicleId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<VehicleEntity>> GetAllVehiclesAsync(int skip, int limit)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM vehicles ORDER BY created_at DESC OFFSET @skip LIMIT @limit");
        cmd.Parameters.AddWithValue("skip", skip);
        cmd.Parameters.AddWithValue("limit", limit);
        return await ReadListAsync(cmd);
    }

    public async Task<long> GetCountAsync()
    {
        await using var cmd = _dataSource.CreateCommand("SELECT COUNT(*) FROM vehicles");
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // This is the direct replacement for the old "look up user.VehicleIds,
    // then fetch each vehicle" pattern - since ownership is now a column on
    // this table, "get a user's vehicles" is just a WHERE user_id = ... query.
    public async Task<List<VehicleEntity>> GetAllByUserAsync(Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM vehicles WHERE user_id = @user_id");
        cmd.Parameters.AddWithValue("user_id", userId);
        return await ReadListAsync(cmd);
    }

    // Ownership check used to be "is this vehicle's id in user.VehicleIds?".
    // Now it's "does a row exist with this id AND this user_id?". SELECT 1
    // (rather than SELECT *) just asks Postgres for the cheapest possible
    // existence check; ExecuteScalarAsync returns null if no row matched.
    public async Task<bool> BelongsToUserAsync(Guid vehicleId, Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT 1 FROM vehicles WHERE id = @id AND user_id = @user_id");
        cmd.Parameters.AddWithValue("id", vehicleId);
        cmd.Parameters.AddWithValue("user_id", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    // Builds a vehicleId -> ownerUserId lookup in one round trip, given a
    // batch of vehicle ids (again using ANY(@ids), same as GetByIdsAsync).
    // This replaces manually walking each user's VehicleIds array to figure
    // out who owns what - here ownership is just read straight off the rows.
    public async Task<Dictionary<Guid, Guid>> GetOwnerMapAsync(IReadOnlyCollection<Guid> vehicleIds)
    {
        var result = new Dictionary<Guid, Guid>();
        if (vehicleIds.Count == 0) return result;

        await using var cmd = _dataSource.CreateCommand(
            "SELECT id, user_id FROM vehicles WHERE id = ANY(@ids) AND user_id IS NOT NULL");
        cmd.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = vehicleIds.ToArray() });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetGuid(0)] = reader.GetGuid(1);
        }
        return result;
    }

    // Mongo equivalent: Builders<UserEntity>.Update.Push(u => u.VehicleIds, id)
    // (adding a vehicle to the user's array). Here "attaching" a vehicle to a
    // user is just setting this row's own user_id column - no second document
    // to update, because there's no array living on the user side anymore.
    public async Task<bool> AttachAsync(Guid vehicleId, Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "UPDATE vehicles SET user_id = @user_id WHERE id = @id");
        cmd.Parameters.AddWithValue("id", vehicleId);
        cmd.Parameters.AddWithValue("user_id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    // Mongo equivalent: Builders<UserEntity>.Update.Pull(u => u.VehicleIds, id).
    // "Detaching" just nulls out user_id on this row. The extra
    // "AND user_id = @user_id" guards against detaching a vehicle that's
    // already been reassigned to a different user out from under them.
    public async Task<bool> DetachAsync(Guid vehicleId, Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "UPDATE vehicles SET user_id = NULL WHERE id = @id AND user_id = @user_id");
        cmd.Parameters.AddWithValue("id", vehicleId);
        cmd.Parameters.AddWithValue("user_id", userId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    // Partial update, same idea as UserRepository.UpdateProfileAsync:
    // COALESCE(@param, column) means "if the DTO didn't supply this field
    // (parameter is DBNull), keep the existing column value" - this is the
    // SQL stand-in for Mongo's Builders<T>.Update.Set(...) chain, which only
    // ever touched the fields you explicitly called .Set() for.
    // Each parameter here is added with an explicit NpgsqlDbType instead of
    // AddWithValue, because when the DTO field is null we hand Npgsql a bare
    // DBNull.Value - without a declared type it can't infer what PG column
    // type that DBNull is supposed to correspond to.
    public async Task<bool> UpdateAsync(Guid id, UpdateVehicleDto dto, DateTime updatedAt)
    {
        const string sql = @"
            UPDATE vehicles
            SET license_plate      = COALESCE(@license_plate,      license_plate),
                bought_at          = COALESCE(@bought_at,          bought_at),
                color              = COALESCE(@color,              color),
                mileage            = COALESCE(@mileage,            mileage),
                photo_storage_keys = COALESCE(@photo_storage_keys, photo_storage_keys),
                updated_at         = @updated_at
            WHERE id = @id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.Add(new NpgsqlParameter("license_plate", NpgsqlDbType.Text)
        { Value = NpgsqlReaderExtensions.NullableParam(dto.LicensePlate) });
        cmd.Parameters.Add(new NpgsqlParameter("bought_at", NpgsqlDbType.TimestampTz)
        { Value = NpgsqlReaderExtensions.NullableParam(dto.BoughtAt) });
        cmd.Parameters.Add(new NpgsqlParameter("color", NpgsqlDbType.Text)
        { Value = NpgsqlReaderExtensions.NullableParam(dto.Color) });
        cmd.Parameters.Add(new NpgsqlParameter("mileage", NpgsqlDbType.Integer)
        { Value = NpgsqlReaderExtensions.NullableParam(dto.Mileage) });
        cmd.Parameters.Add(new NpgsqlParameter("photo_storage_keys", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = (object?)dto.PhotoStorageKeys?.ToArray() ?? DBNull.Value });
        cmd.Parameters.AddWithValue("updated_at", updatedAt);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    private static async Task<List<VehicleEntity>> ReadListAsync(NpgsqlCommand cmd)
    {
        var results = new List<VehicleEntity>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    // Manual ordinal-based mapping again (see UserRepository.Map) - column
    // positions here must match SelectColumns exactly. GetEnum<T>() parses the
    // plain-text column back into the C# enum (the reverse of .ToString() in
    // CreateAsync above).
    private static VehicleEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        UserId = r.GetNullableGuid(1),
        LicensePlate = r.GetString(2),
        Vin = r.GetString(3),
        Brand = r.GetString(4),
        Model = r.GetString(5),
        Year = r.GetInt32(6),
        BoughtAt = r.GetNullableDateTime(7),
        WheelDriveType = r.GetEnum<WheelDriveType>(8),
        EngineCapacity = r.GetInt32(9),
        FuelType = r.GetEnum<FuelType>(10),
        TransmissionType = r.GetEnum<TransmissionType>(11),
        EnginePower = r.GetInt32(12),
        Color = r.GetString(13),
        Mileage = r.GetInt32(14),
        PhotoStorageKeys = r.GetNullableStringList(15),
        CreatedAt = r.GetFieldValue<DateTime>(16),
        UpdatedAt = r.GetNullableDateTime(17),
    };
}
