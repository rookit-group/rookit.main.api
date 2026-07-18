using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

public interface IInternalUserProfileRepository
{
    Task CreateAsync(InternalUserProfileEntity profile);
    Task<InternalUserProfileEntity?> GetByUserIdAsync(Guid userId);
    Task<Guid?> GetIdByUserIdAsync(Guid userId);
}

public class InternalUserProfileRepository : IInternalUserProfileRepository
{
    private const string SelectColumns = "id, user_id, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public InternalUserProfileRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task CreateAsync(InternalUserProfileEntity profile)
    {
        const string sql = @"
            INSERT INTO internal_user_profiles (id, user_id, created_at, updated_at)
            VALUES (@id, @user_id, @created_at, @updated_at)";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", profile.Id);
        cmd.Parameters.AddWithValue("user_id", profile.UserId);
        cmd.Parameters.AddWithValue("created_at", profile.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(profile.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<InternalUserProfileEntity?> GetByUserIdAsync(Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM internal_user_profiles WHERE user_id = @user_id");
        cmd.Parameters.AddWithValue("user_id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<Guid?> GetIdByUserIdAsync(Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT id FROM internal_user_profiles WHERE user_id = @user_id");
        cmd.Parameters.AddWithValue("user_id", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result is Guid id ? id : null;
    }

    private static InternalUserProfileEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        UserId = r.GetGuid(1),
        CreatedAt = r.GetFieldValue<DateTime>(2),
        UpdatedAt = r.GetNullableDateTime(3),
    };
}
