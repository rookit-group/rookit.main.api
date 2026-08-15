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
    Task<bool> UpdateProfileAsync(Guid id, string? name, string? email, DateTime updatedAt, NpgsqlConnection? connection = null);
}

// Mongo equivalent of this whole class: IMongoCollection<UserEntity> plus
// Builders<UserEntity>.Filter/Update. There's no driver-level query builder
// here - every method writes its own SQL string with named @parameters, and
// Npgsql only ever substitutes those parameters safely (never string-concats
// user input into SQL - that's how you'd get SQL injection).
public class UserRepository : IUserRepository
{
    // Column order here must match the order fields are read out by ordinal
    // in Map(...) below - unlike Mongo, where BSON field names are matched by
    // name automatically regardless of order.
    private const string SelectColumns =
        "id, name, email, provider_id, phone, picture_url, created_at, updated_at";

    // NpgsqlDataSource is Npgsql's equivalent of IMongoClient - a shared,
    // thread-safe object registered once as a singleton (see Program.cs) that
    // owns a pool of physical connections. CreateCommand(sql) below grabs and
    // returns a pooled connection per call automatically; you don't manage
    // connections by hand.
    private readonly NpgsqlDataSource _dataSource;

    public UserRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // When a connection is supplied the command runs on it (and therefore inside any open
    // transaction on that connection); otherwise it uses the pooled data source and auto-commits.
    private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection? connection)
        => connection is not null ? new NpgsqlCommand(sql, connection) : _dataSource.CreateCommand(sql);

    // Mongo equivalent: _collection.InsertOneAsync(user). Here we write the
    // literal INSERT statement and bind each property to a named parameter.
    public async Task CreateAsync(UserEntity user)
    {
        const string sql = @"
            INSERT INTO users (id, name, email, provider_id, phone, picture_url, created_at, updated_at)
            VALUES (@id, @name, @email, @provider_id, @phone, @picture_url, @created_at, @updated_at)";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", user.Id);
        cmd.Parameters.AddWithValue("name", user.Name);
        // Nullable properties must be sent as DBNull.Value, not C# null - see
        // NpgsqlReaderExtensions.NullableParam for why.
        cmd.Parameters.AddWithValue("email", NpgsqlReaderExtensions.NullableParam(user.Email));
        cmd.Parameters.AddWithValue("provider_id", user.ProviderId);
        cmd.Parameters.AddWithValue("phone", NpgsqlReaderExtensions.NullableParam(user.Phone));
        cmd.Parameters.AddWithValue("picture_url", NpgsqlReaderExtensions.NullableParam(user.PictureUrl));
        cmd.Parameters.AddWithValue("created_at", user.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(user.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    // Mongo equivalent: _collection.Find(u => u.Id == id).FirstOrDefaultAsync().
    // ExecuteReaderAsync() gives back a forward-only cursor (NpgsqlDataReader);
    // ReadAsync() advances to the first row and returns false if there wasn't one.
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

    // Mongo equivalent: .Find(...).Skip(skip).Limit(limit). Postgres calls
    // these OFFSET/LIMIT instead, same meaning.
    public async Task<List<UserEntity>> GetAllUsersPagedAsync(int skip, int limit)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM users ORDER BY name DESC OFFSET @skip LIMIT @limit");
        cmd.Parameters.AddWithValue("skip", skip);
        cmd.Parameters.AddWithValue("limit", limit);
        return await ReadListAsync(cmd);
    }

    // ExecuteScalarAsync() returns just the single value of the first column
    // of the first row (here, the COUNT) rather than a full row/document.
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

    // Mongo equivalent: Builders<UserEntity>.Update.Set(...) chained only for
    // the fields that were actually provided. There's no update-builder API
    // in raw SQL, so COALESCE(@name, name) does the same job: "use the new
    // value if one was passed, otherwise keep the existing column value" -
    // passing null for a field leaves that column untouched.
    // ExecuteNonQueryAsync() returns the number of rows affected, which is
    // how we know whether a matching row existed (Mongo's MatchedCount).
    public async Task<bool> UpdateProfileAsync(Guid id, string? name, string? email, DateTime updatedAt, NpgsqlConnection? connection = null)
    {
        const string sql = @"
            UPDATE users
            SET name       = COALESCE(@name, name),
                email      = COALESCE(@email, email),
                updated_at = @updated_at
            WHERE id = @id";

        await using var cmd = CreateCommand(sql, connection);
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

    // Manual row -> object mapping, since Npgsql has no auto-deserialization.
    // Columns are read by *position* (0, 1, 2...) matching SelectColumns
    // above exactly - unlike Mongo, which matches BSON fields by name, so
    // reordering SelectColumns without updating these indexes breaks this.
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
