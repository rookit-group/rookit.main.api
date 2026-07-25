using Microsoft.AspNetCore.Authorization;

namespace MainHub.Api.Authorization;

/// <summary>
/// Authorization requirement carrying the single scope an endpoint demands. It is satisfied only
/// when the caller's garage token (a) targets the same garage as the route and (b) holds a scope
/// that grants the required one — directly or via the wildcard. See
/// <see cref="ScopeAuthorizationHandler"/> for the check itself.
/// </summary>
public sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    /// <summary>The scope the protected endpoint requires (e.g. <c>role:manage</c>).</summary>
    public string Scope { get; } = scope;
}
