using System.Security.Claims;
using MainHub.Api.Config;
using MainHub.Api.Enums;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Endpoints;

public static class GarageEndpoints
{
  public static void MapGarageEndpoints(this IEndpointRouteBuilder app)
  {
    var garages = app
      .MapGroup("/api/garages")
      .WithTags("Garages")
      .RequireAuthorization(nameof(AuthPolicy.RequireInternalIdentityJwt));

    garages
      .MapGet("", ListMyGaragesAsync)
      .WithSummary("List the garages the authenticated internal user belongs to")
      .Produces<IReadOnlyList<GarageListItemDto>>(StatusCodes.Status200OK);

    garages
      .MapPost("/{garageId}/session", CreateGarageSessionAsync)
      .WithSummary("Open a garage-scoped session and mint a short-lived garage access token")
      .Produces<string>(StatusCodes.Status200OK)
      .Produces(StatusCodes.Status403Forbidden);
  }

  // Stage-1: an authenticated internal user (InternalIdentityJwt) lists the garages they belong to,
  // so the client can offer a garage to open a session for. Scoped to the caller's own memberships;
  // no permission scope is required beyond a valid identity token.
  internal static async Task<IResult> ListMyGaragesAsync(
    ClaimsPrincipal user,
    ITokenService tokenService,
    IGarageMembershipRepository membershipRepository
  )
  {
    var userId = tokenService.GetUserIdFromClaims(user);

    var garages = await membershipRepository.ListUserGaragesAsync(userId);

    var result = garages
      .Select(g => new GarageListItemDto
      {
        GarageId = g.GarageId,
        Name = g.GarageName,
        RoleName = g.RoleName,
      })
      .ToList();

    return Results.Ok(result);
  }

  // Stage-2 of the internal auth flow: an authenticated internal user (InternalIdentityJwt) selects
  // a garage they belong to. Scopes are resolved from the database here — once, at mint time — and
  // embedded in the returned GarageJwt, so subsequent garage requests need no membership lookup.
  internal static async Task<IResult> CreateGarageSessionAsync(
    [FromRoute] Guid garageId,
    ClaimsPrincipal user,
    ITokenService tokenService,
    IPermissionService permissionService,
    IOptions<GarageJwtSettings> garageJwtSettings
  )
  {
    var userId = tokenService.GetUserIdFromClaims(user);

    var scopes = await permissionService.ResolveScopesAsync(userId, garageId);
    if (scopes is null)
    {
      // Null (as opposed to an empty list) means the user is not a member of this garage.
      return Results.Forbid();
    }

    var accessToken = tokenService.GenerateGarageToken(userId, garageId, scopes);

    return Results.Ok(accessToken);
  }
}
