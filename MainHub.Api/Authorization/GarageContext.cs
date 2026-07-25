namespace MainHub.Api.Authorization;

/// <summary>
/// Single source of truth for the garage-context contract shared between token minting and
/// request-time authorization. The garage token embeds the selected garage and the caller's
/// resolved scopes as claims (<see cref="TokenService"/>); the scope handler reads them back and
/// compares the garage claim against the route (<see cref="ScopeAuthorizationHandler"/>). Keeping
/// these strings in one place stops the writer and reader from silently drifting apart, which would
/// break authorization.
/// </summary>
public static class GarageContext
{
    /// <summary>
    /// Route parameter naming the garage on garage-scoped endpoints, i.e. the <c>{garageId}</c> in a
    /// route template such as <c>/api/garages/{garageId}/roles</c>. Route templates are parsed
    /// strings and the <c>[FromRoute] Guid garageId</c> handler parameter binds by its C# name, so
    /// both must spell this the same by convention - this constant documents and enforces that name
    /// for anything that reads the value from <c>RouteValues</c>.
    /// </summary>
    public const string RouteKey = "garageId";

    /// <summary>
    /// Claim naming the garage a stage-2 garage token was minted for. Written at mint time and
    /// checked at authorization time against <see cref="RouteKey"/>.
    /// </summary>
    public const string GarageIdClaim = "garage_id";

    /// <summary>
    /// Claim holding the caller's space-delimited resolved scopes, embedded once at mint time so
    /// request-time permission checks need no database access.
    /// </summary>
    public const string ScopeClaim = "scope";
}
