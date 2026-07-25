namespace MainHub.Api.Config;

/// <summary>
/// Configuration for the garage-scoped JWT — the stage-2 token minted when an internal user
/// selects a garage they belong to. It carries the user's id, the selected <c>garage_id</c>, and
/// the resolved <c>scope</c> claim, and is deliberately short-lived so that permission or
/// membership changes take effect within one token lifetime.
/// </summary>
/// <remarks>
/// There is intentionally no refresh-token setting here: a garage token is re-minted by calling the
/// garage-session endpoint with a valid internal-identity token (which re-resolves scopes from the
/// database), rather than refreshed via its own opaque token.
/// </remarks>
public class GarageJwtSettings
{
    /// <summary>
    /// Gets or sets the secret key used to sign JWT tokens.
    /// Should be a long, random string (at least 32 characters).
    /// </summary>
    public required string SecretKey { get; set; }

    /// <summary>
    /// Gets or sets the issuer of the JWT token.
    /// </summary>
    public required string Issuer { get; set; }

    /// <summary>
    /// Gets or sets the audience of the JWT token.
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// Gets or sets the expiration time in minutes. Kept short (5 minutes in production) because the
    /// token carries permissions; this bounds how long a revoked or changed permission can linger.
    /// </summary>
    public int ExpirationMinutes { get; set; } = 5;
}
