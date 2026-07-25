using System.Security.Claims;
using MainHub.Api.Config;
using MainHub.Api.Enums;
using MainHub.Api.Filters;
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
      .MapPost("", CreateGarageAsync)
      .AddEndpointFilter<ValidationFilter<CreateGarageDto>>()
      .WithSummary("Create a garage (self-serve); the caller becomes its owner")
      .Produces<GarageListItemDto>(StatusCodes.Status201Created)
      .ProducesValidationProblem();

    garages
      .MapPost("/{garageId}/session", CreateGarageSessionAsync)
      .WithSummary("Open a garage-scoped session and mint a short-lived garage access token")
      .Produces<string>(StatusCodes.Status200OK)
      .Produces(StatusCodes.Status403Forbidden);
  }

  // Self-serve garage creation: any authenticated internal user (InternalIdentityJwt) may create a
  // garage and is atomically seeded as its owner (a wildcard-scoped, immutable "Owner" role +
  // membership - see GarageService). No permission scope is required because there is no garage
  // context yet; the identity token is enough. We EnsureAsync the caller's own profile (idempotent,
  // self-referential) so creation never fails just because the profile row is missing. The response
  // reuses GarageListItemDto so the client immediately has the same {garage, role} shape the list
  // endpoint returns and can open a session for the new garage right away.
  internal static async Task<IResult> CreateGarageAsync(
    CreateGarageDto dto,
    ClaimsPrincipal user,
    ITokenService tokenService,
    IInternalUserProfileRepository internalUserProfileRepository,
    IGarageService garageService
  )
  {
    var userId = tokenService.GetUserIdFromClaims(user);
    var ownerProfileId = await internalUserProfileRepository.EnsureAsync(userId, DateTime.UtcNow);

    var result = await garageService.CreateAsync(dto.Name, ownerProfileId);

    var item = new GarageListItemDto
    {
      GarageId = result.Garage.Id,
      Name = result.Garage.Name,
      RoleName = result.OwnerRole.Name,
    };

    return Results.Created($"/api/garages/{result.Garage.Id}", item);
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
