using System.Security.Claims;
using MainHub.Api.Authorization;
using MainHub.Api.Enums;
using MainHub.Api.Filters;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Endpoints;

// Phone-number staff invitation flow. It has two sides that authenticate with different tokens:
//
//   * The garage-management side (/api/garages/{garageId}/invitations) runs under a GarageJwt whose
//     garage matches {garageId}; listing needs staff:read and creating/revoking need staff:manage.
//   * The invitee side (/api/invitations) runs under the stage-1 InternalIdentityJwt: the invited user
//     is signed in but is not (yet) a member of any garage, so they cannot hold a garage token. They
//     list the invitations addressed to their account phone and accept one to become a member.
//
// The unbypassable domain rules (escalation guard, same-garage role, duplicate-invite guard, phone
// match, duplicate-member guard) live in InvitationService; these handlers only translate DTOs, read
// the actor's scopes/identity, and map service exceptions to status codes.
public static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        // ----- Garage-management side (GarageJwt + scopes) -----
        var garageInvitations = app
            .MapGroup("/api/garages/{garageId}/invitations")
            .WithTags("Invitations");

        garageInvitations
            .MapGet("", ListGarageInvitationsAsync)
            .RequireScope(Scope.StaffRead)
            .WithSummary("List a garage's pending invitations")
            .Produces<IReadOnlyList<GarageInvitationDto>>(StatusCodes.Status200OK);

        garageInvitations
            .MapPost("", CreateInvitationAsync)
            .AddEndpointFilter<ValidationFilter<CreateInvitationDto>>()
            .RequireScope(Scope.StaffManage)
            .WithSummary("Invite a person to the garage by phone number with a role")
            .Produces<GarageInvitationDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        garageInvitations
            .MapDelete("/{invitationId}", RevokeInvitationAsync)
            .RequireScope(Scope.StaffManage)
            .WithSummary("Revoke a pending invitation")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        // ----- Invitee side (InternalIdentityJwt) -----
        var myInvitations = app
            .MapGroup("/api/invitations")
            .WithTags("Invitations")
            .RequireAuthorization(nameof(AuthPolicy.RequireInternalIdentityJwt));

        myInvitations
            .MapGet("", ListMyInvitationsAsync)
            .WithSummary("List the invitations addressed to the signed-in user's phone number")
            .Produces<IReadOnlyList<MyInvitationDto>>(StatusCodes.Status200OK);

        myInvitations
            .MapPost("/{invitationId}/accept", AcceptInvitationAsync)
            .WithSummary("Accept an invitation and join the garage as a member")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    internal static async Task<IResult> ListGarageInvitationsAsync(
        [FromRoute] Guid garageId,
        IInvitationRepository invitationRepository)
    {
        var invitations = await invitationRepository.ListByGarageAsync(garageId);
        return Results.Ok(invitations.Select(ToDto).ToList());
    }

    internal static async Task<IResult> CreateInvitationAsync(
        [FromRoute] Guid garageId,
        CreateInvitationDto dto,
        ClaimsPrincipal user,
        ITokenService tokenService,
        IInvitationService invitationService,
        IInvitationRepository invitationRepository)
    {
        var actorScopes = tokenService.GetScopesFromClaims(user);
        try
        {
            var invitation = await invitationService.InviteAsync(garageId, dto.Phone, dto.RoleId, actorScopes);

            // Re-read so the response reflects committed state (resolved role name).
            var item = await invitationRepository.GetGarageInvitationAsync(garageId, invitation.Id);
            return Results.Created($"/api/garages/{garageId}/invitations/{invitation.Id}", ToDto(item!));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException ex)
        {
            // The role does not exist.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            // The role belongs to another garage, or a pending invitation already exists for this phone.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    internal static async Task<IResult> RevokeInvitationAsync(
        [FromRoute] Guid garageId,
        [FromRoute] Guid invitationId,
        IInvitationService invitationService)
    {
        try
        {
            await invitationService.RevokeAsync(garageId, invitationId);
            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    internal static async Task<IResult> ListMyInvitationsAsync(
        ClaimsPrincipal user,
        ITokenService tokenService,
        IUserRepository userRepository,
        IInvitationRepository invitationRepository)
    {
        var userId = tokenService.GetUserIdFromClaims(user);

        var account = await userRepository.GetByIdAsync(userId);
        if (account is null || string.IsNullOrWhiteSpace(account.Phone))
        {
            // No phone on the account means no invitation could be addressed to this user.
            return Results.Ok(new List<MyInvitationDto>());
        }

        var invitations = await invitationRepository.ListByPhoneAsync(account.Phone.Trim());
        return Results.Ok(invitations.Select(ToDto).ToList());
    }

    internal static async Task<IResult> AcceptInvitationAsync(
        [FromRoute] Guid invitationId,
        ClaimsPrincipal user,
        ITokenService tokenService,
        IInvitationService invitationService)
    {
        var userId = tokenService.GetUserIdFromClaims(user);
        try
        {
            await invitationService.AcceptAsync(invitationId, userId);
            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            // No such invitation, or it is not addressed to this user's phone.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            // The user is already a member of the garage.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static GarageInvitationDto ToDto(GarageInvitationListItem i) => new()
    {
        Id = i.Id,
        Phone = i.Phone,
        RoleId = i.RoleId,
        RoleName = i.RoleName,
        CreatedAt = i.CreatedAt,
    };

    private static MyInvitationDto ToDto(MyInvitationListItem i) => new()
    {
        Id = i.Id,
        GarageId = i.GarageId,
        GarageName = i.GarageName,
        RoleId = i.RoleId,
        RoleName = i.RoleName,
        CreatedAt = i.CreatedAt,
    };
}
