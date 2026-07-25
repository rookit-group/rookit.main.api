namespace MainHub.Api.Authorization;

// Shared authorization guards used by services that hand out scopes (RoleService when defining a
// role, MembershipService when assigning one). Kept here so the "you can't grant what you don't
// hold" rule has exactly one implementation.
public static class PermissionGuard
{
    // Escalation guard: every requested scope must be granted by the actor's own scopes. Because
    // Scope.Grants treats the wildcard as granting everything, only a wildcard holder (an owner)
    // can grant a wildcard; a partial admin can never grant beyond what they themselves hold.
    public static void EnsureCanGrant(IEnumerable<string> actorScopes, IReadOnlyList<string> requestedScopes)
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
