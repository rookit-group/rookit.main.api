using MainHub.Api.Authorization;
using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Npgsql;

namespace MainHub.Api.Services;

// Returned from GarageService.CreateAsync so callers get both the new garage and the owner role
// that was seeded for it. Internal service result (not a wire DTO), so it is not TS-generated.
public record GarageCreationResult(GarageEntity Garage, RoleEntity OwnerRole);

public interface IGarageService
{
    // Creates a garage, seeds its Owner role, and assigns the given internal profile to that
    // role - atomically. The profile must already exist.
    Task<GarageCreationResult> CreateAsync(string name, Guid ownerInternalUserProfileId);
}

public class GarageService(
    NpgsqlDataSource dataSource,
    IGarageRepository garageRepository,
    IRoleRepository roleRepository,
    IGarageMembershipRepository membershipRepository
) : IGarageService
{
    // Default name for the auto-seeded owner role. Owners may rename it later; the role's power
    // comes from its wildcard scope, not from this name - nothing in the system keys off it.
    public const string OwnerRoleName = "Owner";

    private const string OwnerRoleDescription =
        "Full access to this garage, including any permissions added in the future.";

    private readonly NpgsqlDataSource _dataSource = dataSource;
    private readonly IGarageRepository _garageRepository = garageRepository;
    private readonly IRoleRepository _roleRepository = roleRepository;
    private readonly IGarageMembershipRepository _membershipRepository = membershipRepository;

    public async Task<GarageCreationResult> CreateAsync(string name, Guid ownerInternalUserProfileId)
    {
        var now = DateTime.UtcNow;

        var garage = new GarageEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = now,
            UpdatedAt = null,
        };

        // The owner role holds the wildcard scope, so it covers every current and future scope
        // without ever needing a re-seed when new scopes are introduced.
        var ownerRole = new RoleEntity
        {
            Id = Guid.NewGuid(),
            GarageId = garage.Id,
            Name = OwnerRoleName,
            Description = OwnerRoleDescription,
            Scopes = [Scope.Wildcard],
            CreatedAt = now,
            UpdatedAt = null,
        };

        var ownerMembership = new GarageMembershipEntity
        {
            InternalUserProfileId = ownerInternalUserProfileId,
            GarageId = garage.Id,
            RoleId = ownerRole.Id,
            CreatedAt = now,
            UpdatedAt = null,
        };

        // One connection, one transaction: all three writes commit together or not at all.
        // Disposing the transaction without committing (i.e. if any write throws) rolls the whole
        // thing back, so a garage can never exist without its owner role and owner membership.
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await _garageRepository.CreateAsync(garage, connection);
        await _roleRepository.CreateAsync(ownerRole, connection);
        await _membershipRepository.AddAsync(ownerMembership, connection);

        await transaction.CommitAsync();

        return new GarageCreationResult(garage, ownerRole);
    }
}
