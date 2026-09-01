using MainHub.Api.Authorization;
using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Npgsql;

namespace MainHub.Api.Services;

public interface IInvitationService
{
    // Creates a pending invitation of the person with the given phone number to a garage, with a role.
    // The invitee does not need to exist yet. Enforces the escalation guard (the actor may only grant a
    // role whose scopes they hold), that the role belongs to the garage, and that there is no existing
    // pending invitation for the same phone in this garage.
    Task<InvitationEntity> InviteAsync(
        Guid garageId, string phone, Guid roleId, IEnumerable<string> actorScopes);

    // Revokes (deletes) a garage's pending invitation. Fails if the invitation does not belong to the garage.
    Task RevokeAsync(Guid garageId, Guid invitationId);

    // Accepts an invitation on behalf of the signed-in user. The invitation's phone must match the
    // user's account phone; the user becomes a member with the invited role and the invitation is
    // consumed (deleted) in the same transaction. Fails if the user is already a member.
    Task AcceptAsync(Guid invitationId, Guid userId);
}

// Phone-number-based staff invitation flow. An invitation lets a garage invite someone who has not
// necessarily signed in yet - they are addressed by phone number and become a member only when they
// accept. The unbypassable domain rules live here:
//   * Escalation guard - you can only invite with a role whose scopes you yourself hold, so only an
//                        owner (wildcard) can invite someone as an Owner.
//   * Same-garage role  - the invited role must belong to the inviting garage.
//   * Duplicate invite  - a garage can hold at most one pending invitation per phone number.
//   * Phone match        - a user can only accept an invitation addressed to their own account phone.
//   * Duplicate member   - accepting when already a member is rejected (the stale invite is cleared).
public class InvitationService(
    NpgsqlDataSource dataSource,
    IInvitationRepository invitationRepository,
    IInternalUserProfileRepository internalUserProfileRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IGarageMembershipRepository membershipRepository
) : IInvitationService
{
    private readonly NpgsqlDataSource _dataSource = dataSource;
    private readonly IInvitationRepository _invitationRepository = invitationRepository;
    private readonly IInternalUserProfileRepository _internalUserProfileRepository = internalUserProfileRepository;
    private readonly IUserRepository _userRepository = userRepository;
    private readonly IRoleRepository _roleRepository = roleRepository;
    private readonly IGarageMembershipRepository _membershipRepository = membershipRepository;

    public async Task<InvitationEntity> InviteAsync(
        Guid garageId, string phone, Guid roleId, IEnumerable<string> actorScopes)
    {
        var role = await GetGarageRoleAsync(roleId, garageId);
        PermissionGuard.EnsureCanGrant(actorScopes, role.Scopes);

        var normalizedPhone = NormalizePhone(phone);

        if (await _invitationRepository.GetByGarageAndPhoneAsync(garageId, normalizedPhone) is not null)
        {
            throw new InvalidOperationException("An invitation for this phone number already exists in this garage.");
        }

        var now = DateTime.UtcNow;
        var invitation = new InvitationEntity
        {
            Id = Guid.NewGuid(),
            GarageId = garageId,
            Phone = normalizedPhone,
            RoleId = roleId,
            CreatedAt = now,
            UpdatedAt = null,
        };

        await _invitationRepository.AddAsync(invitation);
        return invitation;
    }

    public async Task RevokeAsync(Guid garageId, Guid invitationId)
    {
        var invitation = await _invitationRepository.GetByIdAsync(invitationId);
        if (invitation is null || invitation.GarageId != garageId)
        {
            throw new KeyNotFoundException("Invitation not found.");
        }

        await _invitationRepository.DeleteAsync(invitationId);
    }

    public async Task AcceptAsync(Guid invitationId, Guid userId)
    {
        var invitation = await _invitationRepository.GetByIdAsync(invitationId)
            ?? throw new KeyNotFoundException("Invitation not found.");

        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new KeyNotFoundException("Invitation not found.");

        // Only the person the invitation was addressed to may accept it. A user with no phone (or a
        // different phone) can never match, so we report the same not-found result rather than leaking
        // that an invitation exists for someone else's number.
        if (string.IsNullOrWhiteSpace(user.Phone) ||
            !string.Equals(NormalizePhone(user.Phone), invitation.Phone, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException("Invitation not found.");
        }

        var now = DateTime.UtcNow;

        // The invitee's internal profile is created eagerly at login, but EnsureAsync keeps accept safe
        // even if it is somehow missing (idempotent, race-safe).
        var profileId = await _internalUserProfileRepository.EnsureAsync(userId, now);

        if (await _membershipRepository.GetAsync(profileId, invitation.GarageId) is not null)
        {
            // Already a member: the invitation is stale, so clear it and report the conflict.
            await _invitationRepository.DeleteAsync(invitationId);
            throw new InvalidOperationException("You are already a member of this garage.");
        }

        var membership = new GarageMembershipEntity
        {
            InternalUserProfileId = profileId,
            GarageId = invitation.GarageId,
            RoleId = invitation.RoleId,
            CreatedAt = now,
            UpdatedAt = null,
        };

        // Creating the membership and consuming the invitation must be atomic: either the user becomes a
        // member and the invitation is gone, or neither happens.
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await _membershipRepository.AddAsync(membership, connection);
        await _invitationRepository.DeleteAsync(invitationId, connection);

        await transaction.CommitAsync();
    }

    // Resolves a role and asserts it belongs to the target garage, giving a friendly error before the
    // composite FK would otherwise reject a cross-garage assignment.
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

    // Trims surrounding whitespace so an invite typed with stray spaces still matches the stored phone.
    // Both sides (stored invitation phone and the accepting user's phone) are normalized the same way.
    private static string NormalizePhone(string phone) => phone.Trim();
}
