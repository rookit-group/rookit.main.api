using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MainHub.Api.Authorization;
using MainHub.Api.Config;

namespace MainHub.Api.Services;

/// <summary>
/// Defines methods for generating JWT tokens.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a JWT token for admin users with elevated privileges.
    /// </summary>
    /// <param name="userId">The unique identifier of the admin user.</param>
    string GenerateAdminToken(string userId);

    /// <summary>
    /// Generates the stage-1 internal-identity token issued to a company-staff user after login,
    /// before they select a garage. It proves identity only and carries no garage context or scopes.
    /// </summary>
    /// <param name="userId">The internal user id (users.id) the token represents.</param>
    string GenerateInternalIdentityToken(Guid userId);

    /// <summary>
    /// Generates the stage-2 garage-scoped token minted when an internal user opens a garage session.
    /// The resolved <paramref name="scopes"/> are embedded in the token so request-time authorization
    /// is a pure claim check with no database access.
    /// </summary>
    /// <param name="userId">The internal user id the token represents.</param>
    /// <param name="garageId">The garage the token is scoped to.</param>
    /// <param name="scopes">The permission scopes the user holds within the garage.</param>
    string GenerateGarageToken(Guid userId, Guid garageId, IReadOnlyList<string> scopes);

    /// <summary>
    /// Generates a JWT token for the specified user.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    /// <param name="providerId">The provider identifier of the user.</param>
    /// <returns>A JWT token string.</returns>
    string GenerateToken(Guid userId, string providerId);

    /// <summary>
    /// Extracts the user ID from ClaimsPrincipal.
    /// </summary>
    /// <param name="claims">The ClaimsPrincipal containing the JWT claims.</param>
    /// <returns>The user ID extracted from the claims.</returns>
    /// <exception cref="ArgumentException">Thrown when the claims don't contain a valid user ID.</exception>
    Guid GetUserIdFromClaims(ClaimsPrincipal claims);

    /// <summary>
    /// Reads the caller's permission scopes from a garage token's space-delimited <c>scope</c> claim.
    /// Returns an empty list when the claim is absent. Used by garage-scoped endpoints to pass the
    /// actor's own scopes into the service-layer escalation guards.
    /// </summary>
    /// <param name="claims">The ClaimsPrincipal for the current garage-scoped request.</param>
    IReadOnlyList<string> GetScopesFromClaims(ClaimsPrincipal claims);

    /// <summary>
    /// Generates a cryptographically random opaque refresh token string.
    /// </summary>
    string GenerateRefreshToken();
}

/// <summary>
/// Service for generating JWT tokens.
/// Note: Token validation is handled automatically by ASP.NET Core's JWT Bearer middleware.
/// </summary>
public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly SymmetricSecurityKey _key;
    private readonly AdminJwtSettings _adminJwtSettings;
    private readonly SymmetricSecurityKey _adminKey;
    private readonly InternalIdentityJwtSettings _internalIdentityJwtSettings;
    private readonly SymmetricSecurityKey _internalIdentityKey;
    private readonly GarageJwtSettings _garageJwtSettings;
    private readonly SymmetricSecurityKey _garageKey;

    public TokenService(
        IOptions<JwtSettings> jwtSettings,
        IOptions<AdminJwtSettings> adminJwtSettings,
        IOptions<InternalIdentityJwtSettings> internalIdentityJwtSettings,
        IOptions<GarageJwtSettings> garageJwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        _adminJwtSettings = adminJwtSettings.Value;
        _adminKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_adminJwtSettings.SecretKey));
        _internalIdentityJwtSettings = internalIdentityJwtSettings.Value;
        _internalIdentityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_internalIdentityJwtSettings.SecretKey));
        _garageJwtSettings = garageJwtSettings.Value;
        _garageKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_garageJwtSettings.SecretKey));
    }

    public string GenerateAdminToken(string userId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("userId", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(_adminKey, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_adminJwtSettings.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _adminJwtSettings.Issuer,
            audience: _adminJwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateInternalIdentityToken(Guid userId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("userId", userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(_internalIdentityKey, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_internalIdentityJwtSettings.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _internalIdentityJwtSettings.Issuer,
            audience: _internalIdentityJwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateGarageToken(Guid userId, Guid garageId, IReadOnlyList<string> scopes)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("userId", userId.ToString()),
            new Claim(GarageContext.GarageIdClaim, garageId.ToString()),
            // Scopes are space-delimited in a single claim (OAuth convention); request-time
            // authorization splits this and checks it via Scope.Grants with no database access.
            new Claim(GarageContext.ScopeClaim, string.Join(' ', scopes)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credentials = new SigningCredentials(_garageKey, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_garageJwtSettings.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _garageJwtSettings.Issuer,
            audience: _garageJwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateToken(Guid userId, string providerId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("userId", userId.ToString()),
            new Claim("providerId", providerId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // if (!string.IsNullOrEmpty(email))
        // {
        //     claims.Add(new Claim(ClaimTypes.Email, email));
        //     claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        // }

        // if (!string.IsNullOrEmpty(name))
        // {
        //     claims.Add(new Claim(ClaimTypes.Name, name));
        //     claims.Add(new Claim(JwtRegisteredClaimNames.Name, name));
        // }

        var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid GetUserIdFromClaims(ClaimsPrincipal claims)
    {
        if (claims == null)
        {
            throw new ArgumentNullException(nameof(claims));
        }

        // Try to get userId from the claims
        var userIdClaim = claims.FindFirst("userId") ?? claims.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            throw new ArgumentException("Claims do not contain a valid user ID.", nameof(claims));
        }

        return userId;
    }

    public string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public IReadOnlyList<string> GetScopesFromClaims(ClaimsPrincipal claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var scopeClaim = claims.FindFirst(GarageContext.ScopeClaim)?.Value;
        if (string.IsNullOrWhiteSpace(scopeClaim))
        {
            return [];
        }

        // Scopes are stored space-delimited in a single claim (OAuth convention), mirroring how
        // GenerateGarageToken writes them.
        return scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

