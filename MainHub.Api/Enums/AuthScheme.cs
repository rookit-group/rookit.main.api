namespace MainHub.Api.Enums;

/// <summary>
/// JWT bearer authentication scheme names. Used to register schemes with
/// <c>AddJwtBearer</c> and to reference them from policies.
/// </summary>
public enum AuthScheme
{
  MobileJwt,
  WebJwt,
  AdminJwt
}
