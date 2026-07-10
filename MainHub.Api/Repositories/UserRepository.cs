using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

public interface IUserRepository
{
    Task CreateAsync(UserEntity user);
    Task<UserEntity?> GetByIdAsync(Guid id);
    Task<UserEntity?> GetByProviderIdAsync(string providerId);
    Task<List<UserEntity>> GetAllAsync();
    Task<List<UserEntity>> GetAllUsersPagedAsync(int skip, int limit);
    Task<long> CountAllUsersAsync();
    Task DeleteAsync(Guid id);
    Task<bool> UpdateProfileAsync(Guid id, string? name, string? email, DateTime updatedAt);
}

public class UserRepository : IUserRepository
{
    private const string SelectColumns =
        "id, name, email, provider_id, phone, picture_url, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task CreateAsync(UserEntity user)
    {
        const string sql = @"
            INSERT INTO users (id, name, email, provider_id, phone, picture_url, created_at, updated_at)
            VALUES (@id, @name, @email, @provider_id, @phone, @picture_url, @created_at, @updated_at)";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("name", user.Name);
        cmd.Parameters.AddWithValue("email", NpgsqlReaderExtensions.NullableParam(user.Email));
        cmd.Parameters.AddWithValue("provider_id", user.ProviderId);
        cmd.Parameters.AddWithValue("phone", NpgsqlReaderExtensions.NullableParam(user.Phone));
        cmd.Parameters.AddWithValue("picture_url", NpgsqlReaderExtensions.NullableParam(user.PictureUrl));
        cmd.Parameters.AddWithValue("created_at", user.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(user.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<UserEntity?> GetByIdAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM users WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<UserEntity?> GetByProviderIdAsync(string providerId)
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM users WHERE provider_id = @provider_id");
        cmd.Parameters.AddWithValue("provider_id", providerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<List<UserEntity>> GetAllAsync()
    {
        await using var cmd = _dataSource.CreateCommand($"SELECT {SelectColumns} FROM users");
        return await ReadListAsync(cmd);
    }

    public async Task<List<UserEntity>> GetAllUsersPagedAsync(int skip, int limit)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM users ORDER BY name DESC OFFSET @skip LIMIT @limit");
        cmd.Parameters.AddWithValue("skip", skip);
        cmd.Parameters.AddWithValue("limit", limit);
        return await ReadListAsync(cmd);
    }

    public async Task<long> CountAllUsersAsync()
    {
        await using var cmd = _dataSource.CreateCommand("SELECT COUNT(*) FROM users");
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand("DELETE FROM users WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateProfileAsync(Guid id, string? name, string? email, DateTime updatedAt)
    {
        const string sql = @"
            UPDATE users
            SET name       = COALESCE(@name, name),
                email      = COALESCE(@email, email),
                updated_at = @updated_at
            WHERE id = @id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", NpgsqlReaderExtensions.NullableParam(name));
        cmd.Parameters.AddWithValue("email", NpgsqlReaderExtensions.NullableParam(email));
        cmd.Parameters.AddWithValue("updated_at", updatedAt);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    private static async Task<List<UserEntity>> ReadListAsync(NpgsqlCommand cmd)
    {
        var results = new List<UserEntity>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    private static UserEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        Name = r.GetString(1),
        Email = r.GetNullableString(2),
        ProviderId = r.GetString(3),
        Phone = r.GetNullableString(4),
        PictureUrl = r.GetNullableString(5),
        CreatedAt = r.GetFieldValue<DateTime>(6),
        UpdatedAt = r.GetNullableDateTime(7),
    };
}
