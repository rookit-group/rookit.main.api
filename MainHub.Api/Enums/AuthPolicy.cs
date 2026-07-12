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
  /// Only allows access to endpoints with a valid web JWT token.
  /// </summary>
  RequireWebJwt,

  /// <summary>
  /// Only allows access to endpoints with a valid admin JWT token and the user must be an admin.
  /// </summary>
  RequireAdminJwt
}