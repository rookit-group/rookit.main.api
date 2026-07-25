using System.Security.Claims;
using MainHub.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure-logic tests for the garage-scope authorization handler: no database or HTTP harness needed,
// the handler is driven directly with a hand-built AuthorizationHandlerContext. Covers both checks
// the handler performs - garage context (route garageId == token garage_id) and the scope grant.
public class ScopeAuthorizationHandlerTests
{
    private const string Garage = "11111111-1111-1111-1111-111111111111";
    private const string OtherGarage = "22222222-2222-2222-2222-222222222222";

    // Builds the authorization context the handler reads: an HttpContext (as the resource) carrying
    // the route garageId, plus a user carrying the garage_id and scope claims. Any argument passed as
    // null omits that route value / claim entirely, to exercise the missing-data branches.
    private static AuthorizationHandlerContext Context(
        string? routeGarageId,
        string? tokenGarageId,
        string? scopeClaim,
        string requiredScope,
        bool withHttpContext = true)
    {
        var claims = new List<Claim>();
        if (tokenGarageId is not null) claims.Add(new Claim(GarageContext.GarageIdClaim, tokenGarageId));
        if (scopeClaim is not null) claims.Add(new Claim(GarageContext.ScopeClaim, scopeClaim));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        object? resource = null;
        if (withHttpContext)
        {
            var http = new DefaultHttpContext { User = user };
            if (routeGarageId is not null)
            {
                http.Request.RouteValues[GarageContext.RouteKey] = routeGarageId;
            }
            resource = http;
        }

        return new AuthorizationHandlerContext([new ScopeRequirement(requiredScope)], user, resource);
    }

    private static async Task<bool> SucceedsAsync(AuthorizationHandlerContext context)
    {
        await new ScopeAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_when_garage_matches_and_scope_directly_held()
    {
        var context = Context(Garage, Garage, $"{Scope.StaffRead} {Scope.RoleManage}", Scope.RoleManage);
        Assert.True(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Succeeds_when_garage_matches_and_caller_holds_wildcard()
    {
        var context = Context(Garage, Garage, Scope.Wildcard, Scope.StaffManage);
        Assert.True(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Fails_when_scope_not_held()
    {
        var context = Context(Garage, Garage, Scope.StaffRead, Scope.RoleManage);
        Assert.False(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Fails_when_token_targets_a_different_garage_even_with_wildcard()
    {
        var context = Context(Garage, OtherGarage, Scope.Wildcard, Scope.StaffManage);
        Assert.False(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Fails_when_route_has_no_garage_id()
    {
        var context = Context(routeGarageId: null, Garage, Scope.Wildcard, Scope.StaffManage);
        Assert.False(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Fails_when_token_has_no_garage_id_claim()
    {
        var context = Context(Garage, tokenGarageId: null, Scope.Wildcard, Scope.StaffManage);
        Assert.False(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Fails_when_scope_claim_is_absent()
    {
        var context = Context(Garage, Garage, scopeClaim: null, Scope.StaffManage);
        Assert.False(await SucceedsAsync(context));
    }

    [Fact]
    public async Task Fails_when_resource_is_not_an_http_context()
    {
        var context = Context(Garage, Garage, Scope.Wildcard, Scope.StaffManage, withHttpContext: false);
        Assert.False(await SucceedsAsync(context));
    }
}
