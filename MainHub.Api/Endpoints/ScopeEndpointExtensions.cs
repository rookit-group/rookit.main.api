using MainHub.Api.Authorization;

namespace MainHub.Api.Endpoints;

/// <summary>
/// Endpoint-builder sugar for gating a garage-scoped endpoint on a single scope.
/// </summary>
public static class ScopeEndpointExtensions
{
    /// <summary>
    /// Requires a valid <c>GarageJwt</c> whose garage matches the route's <c>{garageId}</c> and
    /// whose resolved scopes grant <paramref name="scope"/> (directly or via the wildcard). Resolves
    /// to the convention-named policy handled by <see cref="ScopePolicyProvider"/>, so no per-scope
    /// policy needs to be registered.
    /// </summary>
    public static TBuilder RequireScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization($"{ScopePolicyProvider.Prefix}{scope}");
}
