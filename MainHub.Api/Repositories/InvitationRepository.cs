using MainHub.Api.Data;
using MainHub.Api.Models;
using Npgsql;

namespace MainHub.Api.Repositories;

// Read projection for "the pending invitations of a garage": the invitation plus the resolved role
// name. Not a wire DTO - the endpoint maps it to GarageInvitationDto.
public record GarageInvitationListItem(
    Guid Id, string Phone, Guid RoleId, string RoleName, DateTime CreatedAt);

// Read projection for "the invitations addressed to a phone number": each invitation plus the garage
// name and role name the invitee would join as. Not a wire DTO - the endpoint maps it to MyInvitationDto.
public record MyInvitationListItem(
    Guid Id, Guid GarageId, string GarageName, Guid RoleId, string RoleName, DateTime CreatedAt);

// Init-level repository: only the methods a consumer actually needs today. New queries are added
// (with a matching integration test) when a service or endpoint requires them, rather than speculatively.
public interface IInvitationRepository
{
    Task AddAsync(InvitationEntity invitation, NpgsqlConnection? connection = null);
    Task<InvitationEntity?> GetByIdAsync(Guid id);
    Task<InvitationEntity?> GetByGarageAndPhoneAsync(Guid garageId, string phone);
    Task<GarageInvitationListItem?> GetGarageInvitationAsync(Guid garageId, Guid id);
    Task<IReadOnlyList<GarageInvitationListItem>> ListByGarageAsync(Guid garageId);
    Task<IReadOnlyList<MyInvitationListItem>> ListByPhoneAsync(string phone);
    Task<bool> DeleteAsync(Guid id, NpgsqlConnection? connection = null);
}

public class InvitationRepository : IInvitationRepository
{
    private const string SelectColumns =
        "id, garage_id, phone, role_id, created_at, updated_at";

    // Shared FROM/JOIN for the garage-facing projection (list + single), so both queries read the same
    // columns in the same order and MapGarageItem can decode them positionally.
    private const string GarageSelect = @"
        SELECT i.id, i.phone, r.id, r.name, i.created_at
        FROM invitations i
        JOIN roles r ON r.id = i.role_id AND r.garage_id = i.garage_id";

    private readonly NpgsqlDataSource _dataSource;

    public InvitationRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    // When a connection is supplied the command runs on it (and therefore inside any open
    // transaction on that connection); otherwise it uses the pooled data source and auto-commits.
    private NpgsqlCommand CreateCommand(string sql, NpgsqlConnection? connection)
        => connection is not null ? new NpgsqlCommand(sql, connection) : _dataSource.CreateCommand(sql);

    public async Task AddAsync(InvitationEntity invitation, NpgsqlConnection? connection = null)
    {
        const string sql = @"
            INSERT INTO invitations (id, garage_id, phone, role_id, created_at, updated_at)
            VALUES (@id, @garage_id, @phone, @role_id, @created_at, @updated_at)";

        await using var cmd = CreateCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", invitation.Id);
        cmd.Parameters.AddWithValue("garage_id", invitation.GarageId);
        cmd.Parameters.AddWithValue("phone", invitation.Phone);
        cmd.Parameters.AddWithValue("role_id", invitation.RoleId);
        cmd.Parameters.AddWithValue("created_at", invitation.CreatedAt);
        cmd.Parameters.AddWithValue("updated_at", NpgsqlReaderExtensions.NullableParam(invitation.UpdatedAt));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<InvitationEntity?> GetByIdAsync(Guid id)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM invitations WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    // Used by the invite guard to reject a second pending invitation for the same phone in a garage
    // (a friendly error in front of the UNIQUE(garage_id, phone) constraint).
    public async Task<InvitationEntity?> GetByGarageAndPhoneAsync(Guid garageId, string phone)
    {
        await using var cmd = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM invitations WHERE garage_id = @garage_id AND phone = @phone");
        cmd.Parameters.AddWithValue("garage_id", garageId);
        cmd.Parameters.AddWithValue("phone", phone);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    // Fetches a single garage invitation's projection (same shape as the list). Returns null when the
    // invitation does not exist in that garage. Used to build the response after creating an invitation
    // so it reflects committed state (resolved role name).
    public async Task<GarageInvitationListItem?> GetGarageInvitationAsync(Guid garageId, Guid id)
    {
        const string sql = $@"
            {GarageSelect}
            WHERE i.garage_id = @garage_id AND i.id = @id";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapGarageItem(reader) : null;
    }

    // Lists a garage's outstanding invitations with the resolved role name, newest first. Returns an
    // empty list when the garage has none. Backs the staff invitation list (staff:read).
    public async Task<IReadOnlyList<GarageInvitationListItem>> ListByGarageAsync(Guid garageId)
    {
        const string sql = $@"
            {GarageSelect}
            WHERE i.garage_id = @garage_id
            ORDER BY i.created_at DESC";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("garage_id", garageId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var items = new List<GarageInvitationListItem>();
        while (await reader.ReadAsync())
        {
            items.Add(MapGarageItem(reader));
        }
        return items;
    }

    // Lists every invitation addressed to a phone number, with each garage name and role name, newest
    // first. Returns an empty list when there are none. Backs the invitee's "my invitations" screen.
    public async Task<IReadOnlyList<MyInvitationListItem>> ListByPhoneAsync(string phone)
    {
        const string sql = @"
            SELECT i.id, g.id, g.name, r.id, r.name, i.created_at
            FROM invitations i
            JOIN garages g ON g.id = i.garage_id
            JOIN roles r ON r.id = i.role_id AND r.garage_id = i.garage_id
            WHERE i.phone = @phone
            ORDER BY i.created_at DESC";

        await using var cmd = _dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("phone", phone);
        await using var reader = await cmd.ExecuteReaderAsync();

        var items = new List<MyInvitationListItem>();
        while (await reader.ReadAsync())
        {
            items.Add(new MyInvitationListItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTime>(5)));
        }
        return items;
    }

    // Removes an invitation. Returns false when there was no such invitation. Used by both revoke and
    // accept (accept passes the transaction connection so the delete and the membership insert commit
    // atomically).
    public async Task<bool> DeleteAsync(Guid id, NpgsqlConnection? connection = null)
    {
        await using var cmd = CreateCommand("DELETE FROM invitations WHERE id = @id", connection);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static InvitationEntity Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        GarageId = r.GetGuid(1),
        Phone = r.GetString(2),
        RoleId = r.GetGuid(3),
        CreatedAt = r.GetFieldValue<DateTime>(4),
        UpdatedAt = r.GetNullableDateTime(5),
    };

    // Decodes the GarageSelect columns positionally: invitation id, phone, role id, role name, created_at.
    private static GarageInvitationListItem MapGarageItem(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetString(1),
        r.GetGuid(2),
        r.GetString(3),
        r.GetFieldValue<DateTime>(4));
}
