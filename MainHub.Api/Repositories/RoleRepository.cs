using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;
using NpgsqlTypes;

namespace MainHub.Api.Repositories;

// Init-level repository: only the methods a consumer actually needs today. New queries are
// added (with a matching integration test) when a service or endpoint requires them, rather
// than speculatively.
public interface IRoleRepository
{
    Task CreateAsync(RoleEntity role, NpgsqlConnection? connection = null);
    Task CreateManyAsync(IReadOnlyList<RoleEntity> roles, NpgsqlConnection? connection = null);
    Task<RoleEntity?> GetByIdAsync(Guid id);
    Task<List<RoleEntity>> ListByGarageAsync(Guid garageId);
    Task<bool> UpdateAsync(RoleEntity role);
    Task<bool> DeleteAsync(Guid id);
}

public class RoleRepository : IRoleRepository
{
    private const string SelectColumns = "id, garage_id, name, description, scopes, is_system, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public RoleRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // When a connection is supplied the command runs on it (and therefore inside any open
    // transaction on that connection); otherwise it uses the pooled data source and auto-commits.
    private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection? connection)
        => connection is not null ? new NpgsqlCommand(sql, connection) : _dataSource.CreateCommand(sql);

    public async Task CreateAsync(RoleEntity role, NpgsqlConnection? connection = null)
    {
        const string sql = @"
            INSERT INTO roles (id, garage_id, name, description, scopes, is_system, created_at, updated_at)
            VALUES (@id, @garage_id, @name, @description, @scopes, @is_system, @created_at, @updated_at)";

        await using var cmd = CreateCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", role.Id);
        cmd.Parameters.AddWithValue("garage_id", role.GarageId);
        cmd.Parameters.AddWithValue("name", role.Name);
        cmd.Parameters.AddWithValue("description", NpgsqlReaderExtensions.NullableParam(role.Description));
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = role.Scopes.ToArray() });
        cmd.Parameters.AddWithValue("is_system", role.IsSystem);
        cmd.Parameters.AddWithValue("created_at", role.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(role.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    // Bulk-inserts roles in a single round-trip via a batch. When a connection is supplied every
    // command runs on it (inside any open transaction); otherwise a dedicated connection is opened
    // so the batch is atomic. A no-op for an empty list.
    public async Task CreateManyAsync(IReadOnlyList<RoleEntity> roles, NpgsqlConnection? connection = null)
    {
        if (roles.Count == 0) return;

        const string sql = @"
            INSERT INTO roles (id, garage_id, name, description, scopes, is_system, created_at, updated_at)
            VALUES (@id, @garage_id, @name, @description, @scopes, @is_system, @created_at, @updated_at)";

        var ownsConnection = connection is null;
        var conn = connection ?? await _dataSource.OpenConnectionAsync();
        try
        {
            await using var batch = new NpgsqlBatch(conn);
            foreach (var role in roles)
            {
                var command = new NpgsqlBatchCommand(sql);
                command.Parameters.AddWithValue("id", role.Id);
                command.Parameters.AddWithValue("garage_id", role.GarageId);
                command.Parameters.AddWithValue("name", role.Name);
                command.Parameters.AddWithValue("description", NpgsqlReaderExtensions.NullableParam(role.Description));
                command.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = role.Scopes.ToArray() });
                command.Parameters.AddWithValue("is_system", role.IsSystem);
                command.Parameters.AddWithValue("created_at", role.CreatedAt);
                command.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(role.UpdatedAt));
                batch.BatchCommands.Add(command);
            }
            await batch.ExecuteNonQueryAsync();
        }
        finally
        {
            if (ownsConnection) await conn.DisposeAsync();
        }
    }

    public async Task<RoleEntity?> GetByIdAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM roles WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    // Lists every role in a garage, newest last, so the owner (seeded first) sorts to the top of
    // a chronological list. Consumed by the role:read listing.
    public async Task<List<RoleEntity>> ListByGarageAsync(Guid garageId)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM roles WHERE garage_id = @garage_id ORDER BY created_at");
        cmd.Parameters.AddWithValue("garage_id", garageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var roles = new List<RoleEntity>();
        while (await reader.ReadAsync()) roles.Add(Map(reader));
        return roles;
    }

    // Updates the mutable fields of a role (garage_id is immutable). Returns false when no row
    // matched, letting the service surface a not-found.
    public async Task<bool> UpdateAsync(RoleEntity role)
    {
        const string sql = @"
            UPDATE roles
            SET name = @name, description = @description, scopes = @scopes, updated_at = @updated_at
            WHERE id = @id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", role.Id);
        cmd.Parameters.AddWithValue("name", role.Name);
        cmd.Parameters.AddWithValue("description", NpgsqlReaderExtensions.NullableParam(role.Description));
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = role.Scopes.ToArray() });
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(role.UpdatedAt));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Deletes a role by id. Returns false when no row matched. Note: the DB's composite FK
    // (ON DELETE NO ACTION) still blocks deleting a role that a member holds; the service checks
    // that first to give a friendly error, this is the physical delete once that guard passes.
    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand("DELETE FROM roles WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static RoleEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        GarageId = r.GetGuid(1),
        Name = r.GetString(2),
        Description = r.GetNullableString(3),
        Scopes = r.GetNullableStringList(4) ?? [],
        IsSystem = r.GetBoolean(5),
        CreatedAt = r.GetFieldValue<DateTime>(6),
        UpdatedAt = r.GetNullableDateTime(7),
    };
}
