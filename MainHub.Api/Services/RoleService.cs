using MainHub.Api.Authorization;
using MainHub.Api.Models;
using MainHub.Api.Repositories;

namespace MainHub.Api.Services;

public interface IRoleService
{
    Task<RoleEntity> CreateAsync(
        Guid garageId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes);

    Task<List<RoleEntity>> ListByGarageAsync(Guid garageId);

    Task<RoleEntity> UpdateAsync(
        Guid garageId, Guid roleId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes);

    Task DeleteAsync(Guid garageId, Guid roleId);
}

// Owner-facing CRUD over garage roles. The domain rules that must not be bypassable live here
// (not at the endpoint):
//   * System roles       - the platform-seeded Owner role is immutable: it cannot be edited or deleted.
//   * Escalation guard    - you can only grant scopes you yourself hold (wildcard counts).
//   * In-use guard        - a role still assigned to a member cannot be deleted.
// Input-shape validation (name required, scopes belong to the catalog) is NOT done here; it belongs
// to the endpoint request validators. The caller's own scopes (`actorScopes`) are resolved by the
// authorization layer and passed in, keeping RoleService directly testable.
public class RoleService(
    IRoleRepository roleRepository,
    IGarageMembershipRepository membershipRepository
) : IRoleService
{
    private readonly IRoleRepository _roleRepository = roleRepository;
    private readonly IGarageMembershipRepository _membershipRepository = membershipRepository;

    public async Task<RoleEntity> CreateAsync(
        Guid garageId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes)
    {
        PermissionGuard.EnsureCanGrant(actorScopes, scopes);

        var role = new RoleEntity
        {
            Id = Guid.NewGuid(),
            GarageId = garageId,
            Name = name,
            Description = description,
            Scopes = scopes.ToList(),
            IsSystem = false, // owners create ordinary roles; only the platform seeds system roles
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null,
        };

        await _roleRepository.CreateAsync(role);
        return role;
    }

    public Task<List<RoleEntity>> ListByGarageAsync(Guid garageId) =>
        _roleRepository.ListByGarageAsync(garageId);

    public async Task<RoleEntity> UpdateAsync(
        Guid garageId, Guid roleId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes)
    {
        var role = await GetGarageRoleAsync(roleId, garageId);

        EnsureNotSystemRole(role, "edited");
        PermissionGuard.EnsureCanGrant(actorScopes, scopes);

        role.Name = name;
        role.Description = description;
        role.Scopes = scopes.ToList();
        role.UpdatedAt = DateTime.UtcNow;

        await _roleRepository.UpdateAsync(role);
        return role;
    }

    public async Task DeleteAsync(Guid garageId, Guid roleId)
    {
        var role = await GetGarageRoleAsync(roleId, garageId);

        EnsureNotSystemRole(role, "deleted");

        var assignedCount = await _membershipRepository.CountByRoleAsync(roleId);
        if (assignedCount > 0)
        {
            throw new InvalidOperationException(
                $"Role '{role.Name}' is assigned to {assignedCount} member(s) and cannot be deleted. " +
                "Reassign those members to another role first.");
        }

        await _roleRepository.DeleteAsync(roleId);
    }

    // Loads a role and asserts it belongs to the garage the caller is acting in. A garage token only
    // authorizes its own garage (enforced upstream), so addressing a role from another garage - or a
    // non-existent one - is surfaced as not-found rather than letting a cross-garage edit/delete slip
    // through on a matching roleId.
    private async Task<RoleEntity> GetGarageRoleAsync(Guid roleId, Guid garageId)
    {
        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role is null || role.GarageId != garageId)
        {
            throw new KeyNotFoundException($"Role with ID {roleId} not found in this garage.");
        }
        return role;
    }

    // The Owner role (and any future system role) is owned by the platform and must never be
    // changed through the owner-facing CRUD surface.
    private static void EnsureNotSystemRole(RoleEntity role, string verb)
    {
        if (role.IsSystem)
        {
            throw new InvalidOperationException(
                $"'{role.Name}' is a system role and cannot be {verb}.");
        }
    }
}
