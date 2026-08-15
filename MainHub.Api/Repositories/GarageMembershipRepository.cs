using MainHub.Api.Authorization;
using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

// Read projection for "the garages a given user belongs to": the garage plus the caller's role in it.
// Not a wire DTO - the endpoint maps it to GarageListItemDto.
public record UserGarageListItem(Guid GarageId, string GarageName, string RoleName);

// Read projection for "the members of a given garage": each member's user identity plus the role
// they hold in this garage. Not a wire DTO - the endpoint maps it to StaffMemberDto.
public record GarageStaffListItem(
    Guid UserId, string Name, string? Email, string? PictureUrl, Guid RoleId, string RoleName);

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
    Task<IReadOnlyList<UserGarageListItem>> ListUserGaragesAsync(Guid userId);
    Task<IReadOnlyList<GarageStaffListItem>> ListGarageMembersAsync(Guid garageId);
    Task<GarageStaffListItem?> GetGarageMemberAsync(Guid garageId, Guid userId);
    Task<bool> UpdateRoleAsync(Guid internalUserProfileId, Guid garageId, Guid roleId, DateTime updatedAt, NpgsqlConnection? connection = null);
    Task<bool> RemoveAsync(Guid internalUserProfileId, Guid garageId);
}

public class GarageMembershipRepository : IGarageMembershipRepository
{
    private const string SelectColumns =
        "internal_user_profile_id, garage_id, role_id, created_at, updated_at";

    // Shared FROM/JOIN for the staff projection (list + single-member), so both queries read the
    // same columns in the same order and MapStaff can decode them positionally.
    private const string StaffSelect = @"
        SELECT u.id, u.name, u.email, u.picture_url, r.id, r.name
        FROM internal_user_profiles_garages m
        JOIN internal_user_profiles p ON p.id = m.internal_user_profile_id
        JOIN users u ON u.id = p.user_id
        JOIN roles r ON r.id = m.role_id AND r.garage_id = m.garage_id";

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
    public async Task<bool> UpdateRoleAsync(Guid internalUserProfileId, Guid garageId, Guid roleId, DateTime updatedAt, NpgsqlConnection? connection = null)
    {
        const string sql = @"
            UPDATE internal_user_profiles_garages
            SET role_id = @role_id, updated_at = @updated_at
            WHERE internal_user_profile_id = @pid AND garage_id = @garage_id";

        await using var cmd = CreateCommand(sql, connection);
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

    // Lists every garage the user belongs to, with the user's role name in each, by walking
    // user -> internal profile -> membership -> garage/role. Ordered by garage name for a stable
    // selection list. Returns an empty list when the user is not a member of any garage. This backs
    // the stage-1 "which garages can I open a session for?" screen.
    public async Task<IReadOnlyList<UserGarageListItem>> ListUserGaragesAsync(Guid userId)
    {
        const string sql = @"
            SELECT g.id, g.name, r.name
            FROM internal_user_profiles p
            JOIN internal_user_profiles_garages m ON m.internal_user_profile_id = p.id
            JOIN garages g ON g.id = m.garage_id
            JOIN roles r ON r.id = m.role_id AND r.garage_id = m.garage_id
            WHERE p.user_id = @user_id
            ORDER BY g.name";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("user_id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var items = new List<UserGarageListItem>();
        while (await reader.ReadAsync())
        {
            items.Add(new UserGarageListItem(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }
        return items;
    }

    // Lists every member of a garage with their user identity and the role they hold there, by
    // walking membership -> internal profile -> user, and membership -> role. Ordered by user name
    // for a stable staff screen. Returns an empty list for a garage with no members. Backs the
    // garage staff list (staff:read).
    public async Task<IReadOnlyList<GarageStaffListItem>> ListGarageMembersAsync(Guid garageId)
    {
        const string sql = $@"
            {StaffSelect}
            WHERE m.garage_id = @garage_id
            ORDER BY u.name";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var items = new List<GarageStaffListItem>();
        while (await reader.ReadAsync())
        {
            items.Add(MapStaff(reader));
        }
        return items;
    }

    // Fetches a single garage member's projection (same shape as the list). Returns null when the
    // user is not a member of the garage. Used to build the response after invite/assign so it
    // reflects committed state (resolved role name, current user identity).
    public async Task<GarageStaffListItem?> GetGarageMemberAsync(Guid garageId, Guid userId)
    {
        const string sql = $@"
            {StaffSelect}
            WHERE m.garage_id = @garage_id AND u.id = @user_id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        cmd.Parameters.AddWithValue("user_id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapStaff(reader) : null;
    }

    private static GarageMembershipEntity Map(NpgsqlDataReader r) => new()
    {
        InternalUserProfileId = r.GetGuid(0),
        GarageId = r.GetGuid(1),
        RoleId = r.GetGuid(2),
        CreatedAt = r.GetFieldValue<DateTime>(3),
        UpdatedAt = r.GetNullableDateTime(4),
    };

    // Decodes the StaffSelect columns positionally: user id, name, email?, picture_url?, role id, role name.
    private static GarageStaffListItem MapStaff(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.GetNullableString(2),
        r.GetNullableString(3),
        r.GetGuid(4),
        r.GetString(5));
}
