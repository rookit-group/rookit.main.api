using MainHub.Api.Authorization;
using MainHub.Api.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Verifies the dynamic policy provider: convention-named "scope:{scope}" policies are minted with
// the GarageJwt scheme and a ScopeRequirement, while any other name defers to the default provider.
public class ScopePolicyProviderTests
{
    private static ScopePolicyProvider Provider(Action<AuthorizationOptions>? configure = null)
    {
        var options = new AuthorizationOptions();
        configure?.Invoke(options);
        return new ScopePolicyProvider(Options.Create(options));
    }

    [Fact]
    public async Task Mints_a_garage_scoped_policy_for_a_scope_named_policy()
    {
        var provider = Provider();

        var policy = await provider.GetPolicyAsync($"{ScopePolicyProvider.Prefix}{Scope.RoleManage}");

        Assert.NotNull(policy);
        Assert.Contains(nameof(AuthScheme.GarageJwt), policy!.AuthenticationSchemes);
        var scopeRequirement = Assert.Single(policy.Requirements.OfType<ScopeRequirement>());
        Assert.Equal(Scope.RoleManage, scopeRequirement.Scope);
    }

    [Fact]
    public async Task Preserves_colons_in_the_scope_name()
    {
        var provider = Provider();

        var policy = await provider.GetPolicyAsync($"{ScopePolicyProvider.Prefix}{Scope.StaffManage}");

        var scopeRequirement = Assert.Single(policy!.Requirements.OfType<ScopeRequirement>());
        Assert.Equal("staff:manage", scopeRequirement.Scope);
    }

    [Fact]
    public async Task Falls_back_to_the_default_provider_for_non_scope_policy_names()
    {
        // A statically registered policy is resolved by the fallback provider unchanged.
        var provider = Provider(o => o.AddPolicy(
            nameof(AuthPolicy.RequireGarageJwt),
            p => p.RequireAuthenticatedUser()));

        var policy = await provider.GetPolicyAsync(nameof(AuthPolicy.RequireGarageJwt));

        Assert.NotNull(policy);
        Assert.Empty(policy!.Requirements.OfType<ScopeRequirement>());
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_non_scope_policy_name()
    {
        var provider = Provider();

        Assert.Null(await provider.GetPolicyAsync("does-not-exist"));
    }
}
