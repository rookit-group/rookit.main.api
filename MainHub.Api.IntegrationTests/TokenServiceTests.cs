using System.IdentityModel.Tokens.Jwt;
using MainHub.Api.Authorization;
using MainHub.Api.Config;
using MainHub.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure unit tests for the two token generators added for the internal/garage auth flow. No database
// is needed: they assert the shape of the minted JWT (issuer/audience and the claims consumers read).
public class TokenServiceTests
{
    private const string SecretKey = "test-secret-key-that-is-at-least-32-bytes-long!!";

    private static TokenService BuildSut() => new(
        Options.Create(new JwtSettings
        {
            SecretKey = SecretKey,
            Issuer = "mobile-issuer",
            Audience = "mobile-aud",
        }),
        Options.Create(new AdminJwtSettings
        {
            SecretKey = SecretKey,
            Issuer = "admin-issuer",
            Audience = "admin-aud",
        }),
        Options.Create(new InternalIdentityJwtSettings
        {
            SecretKey = SecretKey,
            Issuer = "identity-issuer",
            Audience = "identity-aud",
        }),
        Options.Create(new GarageJwtSettings
        {
            SecretKey = SecretKey,
            Issuer = "garage-issuer",
            Audience = "garage-aud",
        }));

    [Fact]
    public void GenerateInternalIdentityToken_embeds_the_user_id_and_no_garage_context()
    {
        var sut = BuildSut();
        var userId = Guid.NewGuid();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(sut.GenerateInternalIdentityToken(userId));

        Assert.Equal("identity-issuer", jwt.Issuer);
        Assert.Contains("identity-aud", jwt.Audiences);
        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == "userId").Value);
        // Stage-1 token proves identity only: it must not carry garage context or scopes.
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "garage_id");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "scope");
    }

    [Fact]
    public void GenerateGarageToken_embeds_user_garage_and_space_delimited_scopes()
    {
        var sut = BuildSut();
        var userId = Guid.NewGuid();
        var garageId = Guid.NewGuid();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
            sut.GenerateGarageToken(userId, garageId, [Scope.StaffRead, Scope.RoleManage]));

        Assert.Equal("garage-issuer", jwt.Issuer);
        Assert.Contains("garage-aud", jwt.Audiences);
        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == "userId").Value);
        Assert.Equal(garageId.ToString(), jwt.Claims.Single(c => c.Type == "garage_id").Value);
        Assert.Equal($"{Scope.StaffRead} {Scope.RoleManage}", jwt.Claims.Single(c => c.Type == "scope").Value);
    }

    [Fact]
    public void GenerateGarageToken_embeds_the_wildcard_scope_for_an_owner()
    {
        var sut = BuildSut();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(
            sut.GenerateGarageToken(Guid.NewGuid(), Guid.NewGuid(), [Scope.Wildcard]));

        Assert.Equal(Scope.Wildcard, jwt.Claims.Single(c => c.Type == "scope").Value);
    }
}
