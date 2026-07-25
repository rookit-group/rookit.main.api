using MainHub.Api.Repositories;

namespace MainHub.Api.Services;

public interface IPermissionService
{
    // Resolves the scopes a user holds within a garage. Returns null when the user is not a member
    // of the garage. Called at garage-token mint/refresh time; the returned scopes are embedded in
    // the token, so authorization at request time is a pure claim check with no DB access.
    Task<IReadOnlyList<string>?> ResolveScopesAsync(Guid userId, Guid garageId);
}

// Reads a member's effective scopes for a garage. Deliberately thin: it exists as the seam the
// token layer depends on (rather than reaching into the repository directly), and as the place any
// future cross-cutting rules would live (e.g. a platform super-admin resolving to the wildcard
// without a per-garage membership).
public class PermissionService(IGarageMembershipRepository membershipRepository) : IPermissionService
{
    private readonly IGarageMembershipRepository _membershipRepository = membershipRepository;

    public Task<IReadOnlyList<string>?> ResolveScopesAsync(Guid userId, Guid garageId) =>
        _membershipRepository.GetMemberScopesAsync(userId, garageId);
}
