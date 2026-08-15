using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Npgsql;

namespace MainHub.Api.Services;

public interface IRoleService
{
    // Bulk-seeds a garage's starter roles as part of garage creation. When a connection is supplied
    // the insert runs inside that connection's open transaction so it commits atomically with the
    // rest of garage creation.
    Task CreateManyAsync(IReadOnlyList<RoleEntity> roles, NpgsqlConnection? connection = null);

    Task<List<RoleEntity>> ListByGarageAsync(Guid garageId);
}

// Read-side access to garage roles plus the bulk seed used when a garage is created. Roles are
// created only by the platform (the starter set seeded on garage creation); there is no owner-facing
// create/edit/delete surface.
public class RoleService(
    IRoleRepository roleRepository
) : IRoleService
{
    private readonly IRoleRepository _roleRepository = roleRepository;

    public async Task CreateManyAsync(IReadOnlyList<RoleEntity> roles, NpgsqlConnection? connection = null)
    {
        await _roleRepository.CreateManyAsync(roles, connection);
    }

    public Task<List<RoleEntity>> ListByGarageAsync(Guid garageId) =>
        _roleRepository.ListByGarageAsync(garageId);
}
