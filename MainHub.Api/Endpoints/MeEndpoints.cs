using System.Security.Claims;
using MainHub.Api.Enums;
using MainHub.Api.Services;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Endpoints;

// The authenticated internal user's own identity, keyed off the InternalIdentityJwt. The internal
// (company-staff) web app calls this right after login to render the garage-list header and to show
// the user the id they hand to a garage owner to be invited (the same users.id InviteStaffDto takes).
public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app
            .MapGet("/api/me", GetCurrentUserAsync)
            .WithTags("Me")
            .RequireAuthorization(nameof(AuthPolicy.RequireInternalIdentityJwt))
            .WithSummary("Get the authenticated internal user's own identity")
            .Produces<CurrentUserDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal user,
        ITokenService tokenService,
        IUserService userService)
    {
        var userId = tokenService.GetUserIdFromClaims(user);

        var entity = await userService.GetByIdAsync(userId);
        if (entity is null)
        {
            // The identity token is minted only after the user row is created, so this should not
            // happen in practice - but the user may have been deleted since the token was issued.
            return Results.NotFound();
        }

        return Results.Ok(new CurrentUserDto
        {
            UserId = entity.Id,
            Name = entity.Name,
            Email = entity.Email,
            PictureUrl = entity.PictureUrl,
        });
    }
}
