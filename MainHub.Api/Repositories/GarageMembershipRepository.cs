using MainHub.Api.Authorization;
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
    Task<int> CountMembersWithScopeAsync(Guid garageId, string scope);
    Task<IReadOnlyList<string>?> GetMemberScopesAsync(Guid userId, Guid garageId);
    Task<bool> UpdateRoleAsync(Guid internalUserProfileId, Guid garageId, Guid roleId, DateTime updatedAt);
    Task<bool> RemoveAsync(Guid internalUserProfileId, Guid garageId);
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

    // How many members in a garage hold a role that grants the given scope, counting the wildcard
    // as granting everything. Used by MembershipService's last-admin guard (e.g. "is this the last
    // member who can still manage staff?").
    public async Task<int> CountMembersWithScopeAsync(Guid garageId, string scope)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM internal_user_profiles_garages m
            JOIN roles r ON r.id = m.role_id AND r.garage_id = m.garage_id
            WHERE m.garage_id = @garage_id
              AND (@scope = ANY(r.scopes) OR @wildcard = ANY(r.scopes))";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        cmd.Parameters.AddWithValue("scope", scope);
        cmd.Parameters.AddWithValue("wildcard", Scope.Wildcard);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // Resolves the effective scopes a user holds within a garage in one round trip, by walking
    // user -> internal profile -> membership -> role. Returns null when the user is NOT a member of
    // the garage (so the token layer can refuse to mint a garage token); returns the role's scope
    // list otherwise (which may be empty). This is the resolver PermissionService calls at token
    // mint/refresh time.
    public async Task<IReadOnlyList<string>?> GetMemberScopesAsync(Guid userId, Guid garageId)
    {
        const string sql = @"
            SELECT r.scopes
            FROM internal_user_profiles p
            JOIN internal_user_profiles_garages m ON m.internal_user_profile_id = p.id
            JOIN roles r ON r.id = m.role_id AND r.garage_id = m.garage_id
            WHERE p.user_id = @user_id AND m.garage_id = @garage_id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("user_id", userId);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }
        return reader.GetNullableStringList(0) ?? [];
    }

    // Reassigns a member to a different role. Returns false when the member does not exist. The
    // composite FK still guarantees the new role belongs to the same garage.
    public async Task<bool> UpdateRoleAsync(Guid internalUserProfileId, Guid garageId, Guid roleId, DateTime updatedAt)
    {
        const string sql = @"
            UPDATE internal_user_profiles_garages
            SET role_id = @role_id, updated_at = @updated_at
            WHERE internal_user_profile_id = @pid AND garage_id = @garage_id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("pid", internalUserProfileId);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        cmd.Parameters.AddWithValue("role_id", roleId);
        cmd.Parameters.AddWithValue("updated_at", updatedAt);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Removes a member from a garage. Returns false when there was no such membership.
    public async Task<bool> RemoveAsync(Guid internalUserProfileId, Guid garageId)
    {
        const string sql = @"
            DELETE FROM internal_user_profiles_garages
            WHERE internal_user_profile_id = @pid AND garage_id = @garage_id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("pid", internalUserProfileId);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        return await cmd.ExecuteNonQueryAsync() > 0;
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
