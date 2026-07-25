using Microsoft.AspNetCore.Authorization;

namespace MainHub.Api.Authorization;

/// <summary>
/// Enforces a <see cref="ScopeRequirement"/> against a stage-2 garage token. Every garage-scoped
/// endpoint goes through this handler, which performs two checks with no database round trip:
/// <list type="number">
/// <item><description>
/// <b>Garage context</b> — the garage token is bound to exactly one garage, so the garage id in the
/// route must equal the <c>garage_id</c> claim. This blocks presenting garage A's token to a
/// garage B endpoint.
/// </description></item>
/// <item><description>
/// <b>Permission</b> — the caller's scopes (resolved from the database once, at mint time, and
/// embedded space-delimited in the token) must grant the required scope via
/// <see cref="Scope.Grants"/>. Request-time authorization is therefore a pure claim check.
/// </description></item>
/// </list>
/// </summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement
    )
    {
        // Without an HttpContext there is no route to compare the token against, so we cannot verify
        // garage context - fail closed by leaving the requirement unmet.
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var routeGarageId = http.Request.RouteValues.TryGetValue(GarageContext.RouteKey, out var value)
            ? value?.ToString()
            : null;
        var tokenGarageId = context.User.FindFirst(GarageContext.GarageIdClaim)?.Value;

        if (
            string.IsNullOrEmpty(routeGarageId)
            || string.IsNullOrEmpty(tokenGarageId)
            || !string.Equals(routeGarageId, tokenGarageId, StringComparison.OrdinalIgnoreCase)
        )
        {
            return Task.CompletedTask;
        }

        var heldScopes = (context.User.FindFirst(GarageContext.ScopeClaim)?.Value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (Scope.Grants(heldScopes, requirement.Scope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
