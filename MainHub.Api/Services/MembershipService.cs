using MainHub.Api.Authorization;
using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Npgsql;

namespace MainHub.Api.Services;

public interface IMembershipService
{
    // Adds an already-registered user to a garage with a role. The user must already have signed in
    // (so their internal staff profile exists); inviting an unknown user fails. Fails if they are
    // already a member.
    Task<GarageMembershipEntity> InviteAsync(
        Guid garageId, Guid userId, Guid roleId, IEnumerable<string> actorScopes);

    // Updates an existing member's profile (name/email) and role in one atomic operation. Name and
    // email are optional (null leaves them unchanged); the role is always (re)assigned.
    Task<GarageMembershipEntity> UpdateMemberAsync(
        Guid garageId, Guid userId, string? name, string? email, Guid newRoleId, IEnumerable<string> actorScopes);

    // Removes a member from a garage.
    Task RemoveAsync(Guid garageId, Guid userId);
}

// Owner-facing staff management for a garage. The unbypassable domain rules live here:
//   * Escalation guard  - you can only assign a role whose scopes you yourself hold, so only an
//                         owner (wildcard) can assign the Owner role.
//   * Duplicate guard    - a user can't be invited to a garage they already belong to.
//   * Last-admin guard   - the garage must always keep at least one member who can manage staff
//                          (Scope.StaffManage, wildcard counts), so it can never lock itself out.
// The invitee is identified by userId and must already have an internal_user_profile (created the
// first time they sign in) - you cannot invite someone who has never used the app.
public class MembershipService(
    NpgsqlDataSource dataSource,
    IInternalUserProfileRepository internalUserProfileRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IGarageMembershipRepository membershipRepository
) : IMembershipService
{
    private readonly NpgsqlDataSource _dataSource = dataSource;
    private readonly IInternalUserProfileRepository _internalUserProfileRepository = internalUserProfileRepository;
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IRoleRepository _roleRepository = roleRepository;
    private readonly IGarageMembershipRepository _membershipRepository = membershipRepository;

    public async Task<GarageMembershipEntity> InviteAsync(
        Guid garageId, Guid userId, Guid roleId, IEnumerable<string> actorScopes)
    {
        var role = await GetGarageRoleAsync(roleId, garageId);
        PermissionGuard.EnsureCanGrant(actorScopes, role.Scopes);

        // The invitee must already exist as an internal staff user (profile created at their first
        // login). We never create the profile here - inviting an unknown user is an error.
        var profileId = await _internalUserProfileRepository.GetIdByUserIdAsync(userId)
            ?? throw new KeyNotFoundException("No such user. The user must sign in at least once before they can be invited.");

        if (await _membershipRepository.GetAsync(profileId, garageId) is not null)
        {
            throw new InvalidOperationException("This user is already a member of the garage.");
        }

        var now = DateTime.UtcNow;
        var membership = new GarageMembershipEntity
        {
            InternalUserProfileId = profileId,
            GarageId = garageId,
            RoleId = roleId,
            CreatedAt = now,
            UpdatedAt = null,
        };

        await _membershipRepository.AddAsync(membership);
        return membership;
    }

    public async Task<GarageMembershipEntity> UpdateMemberAsync(
        Guid garageId, Guid userId, string? name, string? email, Guid newRoleId, IEnumerable<string> actorScopes)
    {
        var (profileId, membership) = await GetMemberAsync(garageId, userId);
        var newRole = await GetGarageRoleAsync(newRoleId, garageId);

        PermissionGuard.EnsureCanGrant(actorScopes, newRole.Scopes);

        // Demotion lockout guard: if this member is currently a staff-manager and the new role is
        // not, make sure they aren't the last one who can manage staff.
        var currentRole = await _roleRepository.GetByIdAsync(membership.RoleId)
            ?? throw new KeyNotFoundException($"Role with ID {membership.RoleId} not found.");
        var losingStaffManage =
            Scope.Grants(currentRole.Scopes, Scope.StaffManage) &&
            !Scope.Grants(newRole.Scopes, Scope.StaffManage);
        if (losingStaffManage)
        {
            await EnsureNotLastStaffManagerAsync(garageId);
        }

        var now = DateTime.UtcNow;

        // The role lives on the membership row while name/email live on the shared user row - two
        // tables. One transaction keeps them consistent: either both writes land or neither does.
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await _membershipRepository.UpdateRoleAsync(profileId, garageId, newRoleId, now, connection);
        if (name is not null || email is not null)
        {
            await _userRepository.UpdateProfileAsync(userId, name, email, now, connection);
        }

        await transaction.CommitAsync();

        membership.RoleId = newRoleId;
        membership.UpdatedAt = now;
        return membership;
    }

    public async Task RemoveAsync(Guid garageId, Guid userId)
    {
        var (profileId, membership) = await GetMemberAsync(garageId, userId);

        var role = await _roleRepository.GetByIdAsync(membership.RoleId)
            ?? throw new KeyNotFoundException($"Role with ID {membership.RoleId} not found.");
        if (Scope.Grants(role.Scopes, Scope.StaffManage))
        {
            await EnsureNotLastStaffManagerAsync(garageId);
        }

        await _membershipRepository.RemoveAsync(profileId, garageId);
    }

    // Resolves a role and asserts it belongs to the target garage, giving a friendly error before
    // the composite FK would otherwise reject a cross-garage assignment.
    private async Task<RoleEntity> GetGarageRoleAsync(Guid roleId, Guid garageId)
    {
        var role = await _roleRepository.GetByIdAsync(roleId)
            ?? throw new KeyNotFoundException($"Role with ID {roleId} not found.");
        if (role.GarageId != garageId)
        {
            throw new InvalidOperationException("The role does not belong to this garage.");
        }
        return role;
    }

    private async Task<(Guid ProfileId, GarageMembershipEntity Membership)> GetMemberAsync(Guid garageId, Guid userId)
    {
        var profileId = await _internalUserProfileRepository.GetIdByUserIdAsync(userId)
            ?? throw new KeyNotFoundException("This user is not a member of the garage.");
        var membership = await _membershipRepository.GetAsync(profileId, garageId)
            ?? throw new KeyNotFoundException("This user is not a member of the garage.");
        return (profileId, membership);
    }

    private async Task EnsureNotLastStaffManagerAsync(Guid garageId)
    {
        var staffManagers = await _membershipRepository.CountMembersWithScopeAsync(garageId, Scope.StaffManage);
        if (staffManagers <= 1)
        {
            throw new InvalidOperationException(
                "This is the last member who can manage staff; removing or demoting them would lock the garage out.");
        }
    }
}
