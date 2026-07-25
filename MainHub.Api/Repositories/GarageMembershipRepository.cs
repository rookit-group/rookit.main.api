using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

// Init-level repository: only the methods a consumer actually needs today. New queries are
// added (with a matching integration test) when a service or endpoint requires them, rather
// than speculatively.
public interface IGarageMembershipRepository
{
    Task AddAsync(GarageMembershipEntity membership, NpgsqlConnection? connection = null);
    Task<GarageMembershipEntity?> GetAsync(Guid internalUserProfileId, Guid garageId);
    Task<int> CountByRoleAsync(Guid roleId);
}

public class GarageMembershipRepository : IGarageMembershipRepository
{
    private const string SelectColumns =
        "internal_user_profile_id, garage_id, role_id, created_at, updated_at";

    private readonly NpgsqlDataSource _dataSource;

    public GarageMembershipRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // When a connection is supplied the command runs on it (and therefore inside any open
    // transaction on that connection); otherwise it uses the pooled data source and auto-commits.
    private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection? connection)
        => connection is not null ? new NpgsqlCommand(sql, connection) : _dataSource.CreateCommand(sql);

    public async Task AddAsync(GarageMembershipEntity membership, NpgsqlConnection? connection = null)
    {
        const string sql = @"
            INSERT INTO internal_user_profiles_garages
                (internal_user_profile_id, garage_id, role_id, created_at, updated_at)
            VALUES (@pid, @garage_id, @role_id, @created_at, @updated_at)";

        await using var cmd = CreateCommand(sql, connection);
        cmd.Parameters.AddWithValue("pid", membership.InternalUserProfileId);
        cmd.Parameters.AddWithValue("garage_id", membership.GarageId);
        cmd.Parameters.AddWithValue("role_id", membership.RoleId);
        cmd.Parameters.AddWithValue("created_at", membership.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(membership.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<GarageMembershipEntity?> GetAsync(Guid internalUserProfileId, Guid garageId)
    {
        await using var cmd = _dataSource.CreateCommand(
            $@"SELECT {SelectColumns} FROM internal_user_profiles_garages
               WHERE internal_user_profile_id = @pid AND garage_id = @garage_id");
        cmd.Parameters.AddWithValue("pid", internalUserProfileId);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    // How many members currently hold this role. Used by RoleService to block deleting a role
    // that is still assigned (the friendly guard in front of the DB's ON DELETE NO ACTION FK).
    public async Task<int> CountByRoleAsync(Guid roleId)
    {
        await using var cmd = _dataSource.CreateCommand(
            "SELECT COUNT(*) FROM internal_user_profiles_garages WHERE role_id = @role_id");
        cmd.Parameters.AddWithValue("role_id", roleId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static GarageMembershipEntity Map(NpgsqlDataReader r) => new()
    {
        InternalUserProfileId = r.GetGuid(0),
        GarageId = r.GetGuid(1),
        RoleId = r.GetGuid(2),
        CreatedAt = r.GetFieldValue<DateTime>(3),
        UpdatedAt = r.GetNullableDateTime(4),
    };
}
