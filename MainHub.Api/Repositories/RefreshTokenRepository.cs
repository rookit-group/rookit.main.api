using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

public interface IRefreshTokenRepository
{
    Task CreateAsync(RefreshTokenEntity token);
    Task<RefreshTokenEntity?> GetByTokenAsync(string token);
    Task RevokeAsync(Guid id);
    Task DeleteExpiredAsync();
}

// Same shape as UserRepository: plain SQL strings + named @parameters, no
// query builder, no Mongo Builders<T> equivalent needed since every query
// here is a simple single-table statement.
public class RefreshTokenRepository : IRefreshTokenRepository
{
    // Column order must match the ordinals read in Map(...) below.
    private const string SelectColumns =
        "id, token, user_id, provider_id, expires_at, created_at, is_revoked";

    private readonly NpgsqlDataSource _dataSource;

    public RefreshTokenRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // Mongo equivalent: _collection.InsertOneAsync(token).
    public async Task CreateAsync(RefreshTokenEntity token)
    {
        const string sql = @"
            INSERT INTO refresh_tokens (id, token, user_id, provider_id, expires_at, created_at, is_revoked)
            VALUES (@id, @token, @user_id, @provider_id, @expires_at, @created_at, @is_revoked)";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", token.Id);
        cmd.Parameters.AddWithValue("token", token.Token);
        cmd.Parameters.AddWithValue("user_id", token.UserId);
        cmd.Parameters.AddWithValue("provider_id", token.ProviderId);
        cmd.Parameters.AddWithValue("expires_at", token.ExpiresAt);
        cmd.Parameters.AddWithValue("created_at", token.CreatedAt);
        cmd.Parameters.AddWithValue("is_revoked", token.IsRevoked);
        await cmd.ExecuteNonQueryAsync();
    }

    // Mongo equivalent: .Find(t => t.Token == token).FirstOrDefaultAsync().
    public async Task<RefreshTokenEntity?> GetByTokenAsync(string token)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM refresh_tokens WHERE token = @token");
        cmd.Parameters.AddWithValue("token", token);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    // Mongo equivalent: Builders<T>.Update.Set(t => t.IsRevoked, true) on a
    // single matching document - here just a plain UPDATE ... SET.
    public async Task RevokeAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand(
            "UPDATE refresh_tokens SET is_revoked = true WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // Mongo equivalent: DeleteManyAsync(t => t.ExpiresAt < now) - a bulk
    // delete matching a filter, same idea here with a plain WHERE clause.
    public async Task DeleteExpiredAsync()
    {
        await using var cmd = _dataSource.CreateCommand(
            "DELETE FROM refresh_tokens WHERE expires_at < @now");
        cmd.Parameters.AddWithValue("now", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();
    }

    // Manual ordinal-based row -> object mapping (see UserRepository.Map for
    // the same pattern) - positions must match SelectColumns above exactly.
    private static RefreshTokenEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        Token = r.GetString(1),
        UserId = r.GetGuid(2),
        ProviderId = r.GetString(3),
        ExpiresAt = r.GetFieldValue<DateTime>(4),
        CreatedAt = r.GetFieldValue<DateTime>(5),
        IsRevoked = r.GetBoolean(6),
    };
}
