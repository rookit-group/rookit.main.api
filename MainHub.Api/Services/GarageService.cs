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
    // Creates a garage, seeds its default roles (the system Owner role plus starter Manager and
    // Mechanic roles), and assigns the given internal profile to the Owner role - atomically. The
    // profile must already exist.
    Task<GarageCreationResult> CreateAsync(string name, Guid ownerInternalUserProfileId);
}

public class GarageService(
    NpgsqlDataSource dataSource,
    IGarageRepository garageRepository,
    IRoleService roleService,
    IGarageMembershipRepository membershipRepository
) : IGarageService
{
    private readonly NpgsqlDataSource _dataSource = dataSource;
    private readonly IGarageRepository _garageRepository = garageRepository;
    private readonly IRoleService _roleService = roleService;
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
        // without ever needing a re-seed when new scopes are introduced. It is marked IsSystem so
        // the platform owns it: RoleService refuses to edit or delete it, which prevents an owner
        // from ever stripping its scopes and locking the garage out.
        var ownerRole = new RoleEntity
        {
            Id = Guid.NewGuid(),
            GarageId = garage.Id,
            Name = "Owner",
            Description = "Full access to this garage, including any permissions added in the future.",
            Scopes = [Scope.Wildcard],
            IsSystem = true,
            CreatedAt = now,
            UpdatedAt = null,
        };

        var initialRoles = new List<RoleEntity>
        {
            ownerRole,
            // Starter roles seeded so a new garage is immediately usable without the owner having to
            // hand-build a role set. Unlike Owner these are ordinary (IsSystem = false) data roles the
            // owner can freely rename, re-scope, or delete.
            new ()
            {
                Id = Guid.NewGuid(),
                GarageId = garage.Id,
                Name = "Manager",
                Description = "Manage members",
                Scopes = [Scope.GarageRead, Scope.StaffRead],
                IsSystem = false,
                CreatedAt = now,
                UpdatedAt = null,
            },
            new ()
            {
                Id = Guid.NewGuid(),
                GarageId = garage.Id,
                Name = "Mechanic",
                Description = "View and update vehicles",
                Scopes = [Scope.GarageRead],
                IsSystem = false,
                CreatedAt = now,
                UpdatedAt = null,
            },
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
        await _roleService.CreateManyAsync(initialRoles, connection);
        await _membershipRepository.AddAsync(ownerMembership, connection);

        await transaction.CommitAsync();

        return new GarageCreationResult(garage, ownerRole);
    }
}
