using MainHub.Api.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace MainHub.Api.Authorization;

/// <summary>
/// Supplies one authorization policy per required scope, on demand, so endpoints can demand any
/// scope without a named policy being registered for each. The policy name convention is
/// <c>scope:{scope}</c> (e.g. <c>scope:role:manage</c>); use the
/// <see cref="ScopeEndpointExtensions.RequireScope{TBuilder}"/> helper rather than typing it.
///
/// Every scope policy pins the <see cref="AuthScheme.GarageJwt"/> scheme (so an unauthenticated
/// caller gets 401 and an authenticated-but-unscoped caller gets 403) and adds a
/// <see cref="ScopeRequirement"/>. Any other policy name falls through to the default provider, so
/// the statically registered policies (RequireMobileJwt, RequireGarageJwt, ...) keep working.
/// </summary>
public sealed class ScopePolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    /// <summary>Prefix that marks a convention-named per-scope policy.</summary>
    public const string Prefix = "scope:";

    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var scope = policyName[Prefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(nameof(AuthScheme.GarageJwt))
                .RequireAuthenticatedUser()
                .AddRequirements(new ScopeRequirement(scope))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }
}
