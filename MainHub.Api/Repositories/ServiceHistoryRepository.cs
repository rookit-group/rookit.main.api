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

// This is the repository most affected by the schema change: the Mongo
// document had "Records" embedded directly inside it (one document, one
// atomic write). In Postgres, service_histories and service_history_records
// are two separate tables, so:
//   - reading a ServiceHistoryEntity means a JOIN + grouping the rows back
//     into one object with a Records list (see SelectJoin/ReadGroupedAsync),
//   - writing one means multiple SQL statements that must succeed or fail
//     together, so they run inside an explicit transaction (see CreateAsync/
//     UpdateAsync) - there is no single-call equivalent of Mongo's atomic
//     single-document InsertOneAsync/ReplaceOneAsync here.
public class ServiceHistoryRepository : IServiceHistoryRepository
{
    // LEFT JOIN (not INNER JOIN) so a service history with zero records still
    // comes back as one row (with all r.* columns NULL) instead of disappearing
    // entirely - Mongo would just have an empty Records array in that case.
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

    // Mongo equivalent: _collection.InsertOneAsync(serviceHistory) with
    // Records embedded inline - one atomic write. Here it's two separate
    // inserts (parent row, then each child record row), so we open an
    // explicit transaction: if the records insert fails partway through, the
    // parent insert is rolled back too instead of leaving an orphaned
    // service_histories row with no records.
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

    // Mongo equivalent: replacing the whole document (parent fields + the
    // entire embedded Records array) in one ReplaceOneAsync call. Postgres has
    // no "replace this child array" operation, so instead we: update the
    // parent row, DELETE every existing child record for this parent, then
    // re-insert the new set of records from scratch - all inside one
    // transaction so a partial update can't leave stale/duplicate records.
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

        // No matching parent row - roll back (nothing was changed) and report
        // "not found", same meaning as a zero MatchedCount in Mongo.
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

    // CASCADE on service_history_records' FK (see db/init.sql) means Postgres
    // deletes the child record rows for us automatically here - no need to
    // manually delete records first like you'd have to in application code
    // without a relational cascade.
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

    // Runs one INSERT per record inside the caller's existing transaction
    // (note: reuses `conn`/`tx`, doesn't open its own). This is the "write
    // the embedded array's items" half of Create/UpdateAsync above.
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

    // Reconstructs the "embedded array" shape the app code expects from the
    // flat JOIN result set. Because of the LEFT JOIN, one parent can appear
    // across multiple rows (one per record) - byId/order track "have we
    // already built this parent's entity?" so we only create it once and just
    // append additional records to its Records list as more rows come in.
    // reader.IsDBNull(6) checks whether the "r.*" columns are null for this
    // row, i.e. whether this parent actually has a matching record or the
    // LEFT JOIN just padded it out with nulls (a parent with zero records).
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
