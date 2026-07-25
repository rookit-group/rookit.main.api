using MainHub.Api.Authorization;
using MainHub.Api.Models;
using MainHub.Api.Repositories;

namespace MainHub.Api.Services;

public interface IRoleService
{
    Task<RoleEntity> CreateAsync(
        Guid garageId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes);

    Task<RoleEntity?> GetByIdAsync(Guid id);

    Task<List<RoleEntity>> ListByGarageAsync(Guid garageId);

    Task<RoleEntity> UpdateAsync(
        Guid roleId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes);

    Task DeleteAsync(Guid roleId);
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
        EnsureActorCanGrant(actorScopes, scopes);

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

    public Task<RoleEntity?> GetByIdAsync(Guid id) => _roleRepository.GetByIdAsync(id);

    public Task<List<RoleEntity>> ListByGarageAsync(Guid garageId) =>
        _roleRepository.ListByGarageAsync(garageId);

    public async Task<RoleEntity> UpdateAsync(
        Guid roleId, string name, string? description,
        IReadOnlyList<string> scopes, IEnumerable<string> actorScopes)
    {
        var role = await _roleRepository.GetByIdAsync(roleId)
            ?? throw new KeyNotFoundException($"Role with ID {roleId} not found.");

        EnsureNotSystemRole(role, "edited");
        EnsureActorCanGrant(actorScopes, scopes);

        role.Name = name;
        role.Description = description;
        role.Scopes = scopes.ToList();
        role.UpdatedAt = DateTime.UtcNow;

        await _roleRepository.UpdateAsync(role);
        return role;
    }

    public async Task DeleteAsync(Guid roleId)
    {
        var role = await _roleRepository.GetByIdAsync(roleId)
            ?? throw new KeyNotFoundException($"Role with ID {roleId} not found.");

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

    // Escalation guard: every requested scope must be granted by the actor's own scopes. Because
    // Scope.Grants treats the wildcard as granting everything, only a wildcard holder (an owner)
    // can create/edit a wildcard role; a partial admin can never grant beyond what they hold.
    private static void EnsureActorCanGrant(IEnumerable<string> actorScopes, IReadOnlyList<string> requestedScopes)
    {
        var held = actorScopes as ICollection<string> ?? actorScopes.ToList();
        var notPermitted = requestedScopes.Where(s => !Scope.Grants(held, s)).Distinct().ToList();
        if (notPermitted.Count > 0)
        {
            throw new UnauthorizedAccessException(
                $"You cannot grant scope(s) you do not hold: {string.Join(", ", notPermitted)}.");
        }
    }
}
