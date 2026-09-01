using System.Security.Claims;
using MainHub.Api.Authorization;
using MainHub.Api.Filters;
using MainHub.Api.Repositories;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Endpoints;

// Garage-scoped staff (member) management. Every route runs under a GarageJwt whose garage matches
// the route's {garageId}; the read route needs staff:read and the write routes need staff:manage.
// The unbypassable domain rules (escalation guard, last-staff-manager guard, cross-garage role
// isolation) live in MembershipService; these handlers only translate DTOs, read the actor's scopes,
// and map service exceptions to status codes. New members join via the phone-number invitation flow
// (see InvitationEndpoints); this group covers listing, role/profile changes, and removal.
public static class StaffEndpoints
{
    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app
            .MapGroup("/api/garages/{garageId}/staff")
            .WithTags("Staff");

        staff
            .MapGet("", ListStaffAsync)
            .RequireScope(Scope.StaffRead)
            .WithSummary("List the members of a garage")
            .Produces<IReadOnlyList<StaffMemberDto>>(StatusCodes.Status200OK);

        staff
            .MapPut("/{userId}", UpdateStaffMemberAsync)
            .AddEndpointFilter<ValidationFilter<UpdateStaffMemberDto>>()
            .RequireScope(Scope.StaffManage)
            .WithSummary("Update a member's profile and role")
            .Produces<StaffMemberDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        staff
            .MapDelete("/{userId}", RemoveStaffAsync)
            .RequireScope(Scope.StaffManage)
            .WithSummary("Remove a member from the garage")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    internal static async Task<IResult> ListStaffAsync(
        [FromRoute] Guid garageId,
        IGarageMembershipRepository membershipRepository)
    {
        var members = await membershipRepository.ListGarageMembersAsync(garageId);
        return Results.Ok(members.Select(ToDto).ToList());
    }

    internal static async Task<IResult> UpdateStaffMemberAsync(
        [FromRoute] Guid garageId,
        [FromRoute] Guid userId,
        UpdateStaffMemberDto dto,
        ClaimsPrincipal user,
        ITokenService tokenService,
        IMembershipService membershipService,
        IGarageMembershipRepository membershipRepository)
    {
        var actorScopes = tokenService.GetScopesFromClaims(user);
        try
        {
            await membershipService.UpdateMemberAsync(
                garageId, userId, dto.Name, dto.Email, dto.RoleId, actorScopes);

            // Re-read so the response reflects committed state (updated identity + resolved role name).
            var member = await membershipRepository.GetGarageMemberAsync(garageId, userId);
            return Results.Ok(ToDto(member!));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException ex)
        {
            // Not a member of this garage, or the target role does not exist.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            // Role belongs to another garage, or this would demote the last staff manager.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    internal static async Task<IResult> RemoveStaffAsync(
        [FromRoute] Guid garageId,
        [FromRoute] Guid userId,
        IMembershipService membershipService)
    {
        try
        {
            await membershipService.RemoveAsync(garageId, userId);
            return Results.NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            // Removing this member would leave the garage with no one who can manage staff.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static StaffMemberDto ToDto(GarageStaffListItem m) => new()
    {
        UserId = m.UserId,
        Name = m.Name,
        Email = m.Email,
        PictureUrl = m.PictureUrl,
        RoleId = m.RoleId,
        RoleName = m.RoleName,
    };
}
