using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

public interface IExternalUserProfileRepository
{
    Task CreateAsync(ExternalUserProfileEntity profile);
    Task<ExternalUserProfileEntity?> GetByUserIdAsync(Guid userId);
}

public class ExternalUserProfileRepository : IExternalUserProfileRepository
{
    private const string SelectColumns = "id, user_id, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public ExternalUserProfileRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task CreateAsync(ExternalUserProfileEntity profile)
    {
        const string sql = @"
            INSERT INTO external_user_profiles (id, user_id, created_at, updated_at)
            VALUES (@id, @user_id, @created_at, @updated_at)";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", profile.Id);
        cmd.Parameters.AddWithValue("user_id", profile.UserId);
        cmd.Parameters.AddWithValue("created_at", profile.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(profile.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ExternalUserProfileEntity?> GetByUserIdAsync(Guid userId)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM external_user_profiles WHERE user_id = @user_id");
        cmd.Parameters.AddWithValue("user_id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    private static ExternalUserProfileEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        UserId = r.GetGuid(1),
        CreatedAt = r.GetFieldValue<DateTime>(2),
        UpdatedAt = r.GetNullableDateTime(3),
    };
}
