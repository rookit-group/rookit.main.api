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
    Task CreateAsync(RoleEntity role);
    Task<RoleEntity?> GetByIdAsync(Guid id);
}

public class RoleRepository : IRoleRepository
{
    private const string SelectColumns = "id, garage_id, name, description, scopes, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public RoleRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task CreateAsync(RoleEntity role)
    {
        const string sql = @"
            INSERT INTO roles (id, garage_id, name, description, scopes, created_at, updated_at)
            VALUES (@id, @garage_id, @name, @description, @scopes, @created_at, @updated_at)";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", role.Id);
        cmd.Parameters.AddWithValue("garage_id", role.GarageId);
        cmd.Parameters.AddWithValue("name", role.Name);
        cmd.Parameters.AddWithValue("description", NpgsqlReaderExtensions.NullableParam(role.Description));
        cmd.Parameters.Add(new NpgsqlParameter("scopes", NpgsqlDbType.Array | NpgsqlDbType.Text)
        { Value = role.Scopes.ToArray() });
        cmd.Parameters.AddWithValue("created_at", role.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(role.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<RoleEntity?> GetByIdAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM roles WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static RoleEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        GarageId = r.GetGuid(1),
        Name = r.GetString(2),
        Description = r.GetNullableString(3),
        Scopes = r.GetNullableStringList(4) ?? [],
        CreatedAt = r.GetFieldValue<DateTime>(5),
        UpdatedAt = r.GetNullableDateTime(6),
    };
}
