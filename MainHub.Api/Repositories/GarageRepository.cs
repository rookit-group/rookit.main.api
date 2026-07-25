using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

// Init-level repository: only the methods a consumer actually needs today. New queries are
// added (with a matching integration test) when a service or endpoint requires them, rather
// than speculatively.
public interface IGarageRepository
{
    Task CreateAsync(GarageEntity garage, NpgsqlConnection? connection = null);
    Task<GarageEntity?> GetByIdAsync(Guid id);
}

public class GarageRepository : IGarageRepository
{
    private const string SelectColumns = "id, name, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public GarageRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // When a connection is supplied the command runs on it (and therefore inside any open
    // transaction on that connection); otherwise it uses the pooled data source and auto-commits.
    private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection? connection)
        => connection is not null ? new NpgsqlCommand(sql, connection) : _dataSource.CreateCommand(sql);

    public async Task CreateAsync(GarageEntity garage, NpgsqlConnection? connection = null)
    {
        const string sql = @"
            INSERT INTO garages (id, name, created_at, updated_at)
            VALUES (@id, @name, @created_at, @updated_at)";

        await using var cmd = CreateCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", garage.Id);
        cmd.Parameters.AddWithValue("name", garage.Name);
        cmd.Parameters.AddWithValue("created_at", garage.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(garage.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<GarageEntity?> GetByIdAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM garages WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static GarageEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        Name = r.GetString(1),
        CreatedAt = r.GetFieldValue<DateTime>(2),
        UpdatedAt = r.GetNullableDateTime(3),
    };
}
