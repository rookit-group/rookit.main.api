using MainHub.Api.Data;
using MainHub.Api.Models;
using NpgsqlTypes;
using Npgsql;

namespace MainHub.Api.Repositories;

public interface IServiceHistoryRepository
{
    Task<List<ServiceHistoryEntity>> GetAllByVehicleIdAsync(Guid vehicleId);
    Task<bool> DeleteAsync(Guid serviceHistoryId);
    Task DeleteAllByVehicleIdAsync(Guid vehicleId);
    Task<ServiceHistoryEntity?> GetByIdAsync(Guid vehicleId, Guid serviceHistoryId);
    Task<ServiceHistoryEntity?> GetByServiceHistoryIdAsync(Guid serviceHistoryId);
    Task CreateAsync(ServiceHistoryEntity serviceHistory);
    Task<bool> UpdateAsync(ServiceHistoryEntity serviceHistory);
}

public class ServiceHistoryRepository : IServiceHistoryRepository
{
    private const string SelectJoin = @"
        SELECT sh.id, sh.vehicle_id, sh.title, sh.description, sh.created_at, sh.updated_at,
               r.id, r.service_history_id, r.title, r.description, r.price
        FROM service_histories sh
        LEFT JOIN service_history_records r ON r.service_history_id = sh.id";

    private readonly NpgsqlDataSource _dataSource;

    public ServiceHistoryRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task CreateAsync(ServiceHistoryEntity serviceHistory)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string insertHistory = @"
            INSERT INTO service_histories (id, vehicle_id, title, description, created_at, updated_at)
            VALUES (@id, @vehicle_id, @title, @description, @created_at, @updated_at)";

        await using (var cmd = new NpgsqlCommand(insertHistory, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", serviceHistory.Id);
            cmd.Parameters.AddWithValue("vehicle_id", serviceHistory.VehicleId);
            cmd.Parameters.AddWithValue("title", serviceHistory.Title);
            cmd.Parameters.AddWithValue("description", serviceHistory.Description);
            cmd.Parameters.AddWithValue("created_at", serviceHistory.CreatedAt);
            cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(serviceHistory.UpdatedAt));
            await cmd.ExecuteNonQueryAsync();
        }

        await InsertRecordsAsync(conn, tx, serviceHistory.Id, serviceHistory.Records);
        await tx.CommitAsync();
    }

    public async Task<bool> UpdateAsync(ServiceHistoryEntity serviceHistory)
    {
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        const string updateHistory = @"
            UPDATE service_histories
            SET title       = @title,
                description = @description,
                updated_at  = @updated_at
            WHERE id = @id";

        int rows;
        await using (var cmd = new NpgsqlCommand(updateHistory, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", serviceHistory.Id);
            cmd.Parameters.AddWithValue("title", serviceHistory.Title);
            cmd.Parameters.AddWithValue("description", serviceHistory.Description);
            cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(serviceHistory.UpdatedAt));
            rows = await cmd.ExecuteNonQueryAsync();
        }

        if (rows == 0)
        {
            await tx.RollbackAsync();
            return false;
        }

        await using (var del = new NpgsqlCommand("DELETE FROM service_history_records WHERE service_history_id = @id", conn, tx))
        {
            del.Parameters.AddWithValue("id", serviceHistory.Id);
            await del.ExecuteNonQueryAsync();
        }

        await InsertRecordsAsync(conn, tx, serviceHistory.Id, serviceHistory.Records);
        await tx.CommitAsync();
        return true;
    }

    public async Task<ServiceHistoryEntity?> GetByIdAsync(Guid vehicleId, Guid serviceHistoryId)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"{SelectJoin} WHERE sh.id = @id AND sh.vehicle_id = @vehicle_id");
        cmd.Parameters.AddWithValue("id", serviceHistoryId);
        cmd.Parameters.AddWithValue("vehicle_id", vehicleId);
        var list = await ReadGroupedAsync(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<ServiceHistoryEntity?> GetByServiceHistoryIdAsync(Guid serviceHistoryId)
    {
        await using var cmd = _dataSource.CreateCommand($"{SelectJoin} WHERE sh.id = @id");
        cmd.Parameters.AddWithValue("id", serviceHistoryId);
        var list = await ReadGroupedAsync(cmd);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<List<ServiceHistoryEntity>> GetAllByVehicleIdAsync(Guid vehicleId)
    {
        await using var cmd = _dataSource.CreateCommand($"{SelectJoin} WHERE sh.vehicle_id = @vehicle_id");
        cmd.Parameters.AddWithValue("vehicle_id", vehicleId);
        return await ReadGroupedAsync(cmd);
    }

    public async Task<bool> DeleteAsync(Guid serviceHistoryId)
    {
        await using var cmd = _dataSource.CreateCommand("DELETE FROM service_histories WHERE id = @id");
        cmd.Parameters.AddWithValue("id", serviceHistoryId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task DeleteAllByVehicleIdAsync(Guid vehicleId)
    {
        await using var cmd = _dataSource.CreateCommand("DELETE FROM service_histories WHERE vehicle_id = @vehicle_id");
        cmd.Parameters.AddWithValue("vehicle_id", vehicleId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertRecordsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid serviceHistoryId,
        List<ServiceHistoryRecordModel> records)
    {
        if (records.Count == 0) return;

        const string sql = @"
            INSERT INTO service_history_records (id, service_history_id, title, description, price)
            VALUES (@id, @sh_id, @title, @description, @price)";

        foreach (var record in records)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("id", record.Id);
            cmd.Parameters.AddWithValue("sh_id", serviceHistoryId);
            cmd.Parameters.AddWithValue("title", record.Title);
            cmd.Parameters.AddWithValue("description", record.Description);
            cmd.Parameters.AddWithValue("price", record.Price);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<ServiceHistoryEntity>> ReadGroupedAsync(NpgsqlCommand cmd)
    {
        var byId = new Dictionary<Guid, ServiceHistoryEntity>();
        var order = new List<Guid>();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            if (!byId.TryGetValue(id, out var entity))
            {
                entity = new ServiceHistoryEntity
                {
                    Id = id,
                    VehicleId = reader.GetGuid(1),
                    Title = reader.GetString(2),
                    Description = reader.GetString(3),
                    CreatedAt = reader.GetFieldValue<DateTime>(4),
                    UpdatedAt = reader.GetNullableDateTime(5),
                    Records = [],
                };
                byId[id] = entity;
                order.Add(id);
            }

            if (!reader.IsDBNull(6))
            {
                entity.Records.Add(new ServiceHistoryRecordModel
                {
                    Id = reader.GetGuid(6),
                    ServiceHistoryId = reader.GetGuid(7),
                    Title = reader.GetString(8),
                    Description = reader.GetString(9),
                    Price = reader.GetInt32(10),
                });
            }
        }

        return order.Select(id => byId[id]).ToList();
    }
}
