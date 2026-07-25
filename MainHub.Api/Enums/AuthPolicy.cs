namespace MainHub.Api.Enums;

/// <summary>
/// Authorization policy names applied via <c>RequireAuthorization</c>.
/// Each policy pins one or more <see cref="AuthScheme"/> values.
/// </summary>
public enum AuthPolicy
{
  /// <summary>
  /// Only allows access to endpoints with a valid mobile JWT token.
  /// </summary>
  RequireMobileJwt,

  /// <summary>
  /// Stage 1 for internal (company staff) users: a valid internal-identity JWT (logged in, no
  /// garage selected yet). Authorizes account-level and garage-listing/selection endpoints.
  /// </summary>
  RequireInternalIdentityJwt,

  /// <summary>
  /// Stage 2 for internal users: a valid garage-scoped JWT (a garage has been selected). Carries
  /// the resolved scopes; per-endpoint permission checks build on top of this.
  /// </summary>
  RequireGarageJwt,

  /// <summary>
  /// Only allows access to endpoints with a valid admin JWT token and the user must be an admin.
  /// </summary>
  RequireAdminJwt
}