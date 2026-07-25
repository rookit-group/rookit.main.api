namespace MainHub.Api.Authorization;

/// <summary>
/// The fixed, code-defined permission vocabulary for internal (company staff) users.
///
/// This is the stable contract application code is written against. Roles, by contrast,
/// are DATA: a garage owner creates roles and assigns some subset of these scopes to each.
/// Endpoints check <c>RequirePermission(garageId, Scope.X)</c> and never branch on a role's
/// name — role names are owner-defined and meaningless to the system.
///
/// Adding a capability = add a constant here (and gate the relevant endpoint). Owners can
/// then assign it to their roles. <see cref="All"/> is used to validate owner-submitted
/// scopes so the UI can only assign scopes that actually exist.
/// </summary>
public static class Scope
{
    /// <summary>View garage details/settings.</summary>
    public const string GarageRead = "garage:read";

    /// <summary>Edit garage details/settings.</summary>
    public const string GarageManage = "garage:manage";

    /// <summary>View the garage's roles and their scopes.</summary>
    public const string RoleRead = "role:read";

    /// <summary>Create, edit, delete roles and assign them to staff.</summary>
    public const string RoleManage = "role:manage";

    /// <summary>View the garage's staff (internal members).</summary>
    public const string StaffRead = "staff:read";

    /// <summary>Invite, remove, and change roles of staff.</summary>
    public const string StaffManage = "staff:manage";

    /// <summary>
    /// Wildcard scope meaning "every scope, including any added in the future". The Owner role
    /// seeded on garage creation holds exactly this, so it never needs re-seeding when new
    /// scopes are introduced. Reserved for full-admin roles; grantable only by a caller who
    /// already holds it (see the escalation guard).
    /// </summary>
    public const string Wildcard = "*";

    /// <summary>
    /// Every concrete scope that exists in the system (excludes the <see cref="Wildcard"/>).
    /// Used to validate owner-submitted role scopes.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        GarageRead,
        GarageManage,
        RoleRead,
        RoleManage,
        StaffRead,
        StaffManage,
    };

    /// <summary>
    /// True if <paramref name="heldScopes"/> grants <paramref name="required"/> — either directly
    /// or via the <see cref="Wildcard"/>. This is THE way to check a permission; never compare
    /// against a role name.
    /// </summary>
    public static bool Grants(IEnumerable<string> heldScopes, string required)
    {
        foreach (var s in heldScopes)
        {
            if (s == Wildcard || s == required) return true;
        }
        return false;
    }

    /// <summary>True if every scope in <paramref name="scopes"/> is assignable (a catalog scope or the wildcard).</summary>
    public static bool AreAllKnown(IEnumerable<string> scopes) =>
        scopes.All(s => s == Wildcard || All.Contains(s));

    /// <summary>Returns the scopes that are NOT assignable (empty when all valid).</summary>
    public static IReadOnlyList<string> UnknownScopes(IEnumerable<string> scopes) =>
        scopes.Where(s => s != Wildcard && !All.Contains(s)).Distinct().ToList();
}
