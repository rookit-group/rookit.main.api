using System.Security.Claims;
using MainHub.Api.Authorization;
using MainHub.Api.Filters;
using MainHub.Api.Models;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Endpoints;

// Garage-scoped role management. Every route runs under a GarageJwt whose garage matches the
// route's {garageId} and whose scopes grant the required permission (RequireScope). Read routes
// need role:read; write routes need role:manage. The unbypassable domain rules (system-role
// immutability, escalation guard, in-use-delete guard, cross-garage isolation) live in RoleService;
// these handlers only translate DTOs and map service exceptions to HTTP status codes.
public static class RoleEndpoints
{
    public static void MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var roles = app
            .MapGroup("/api/garages/{garageId}/roles")
            .WithTags("Roles");

        roles
            .MapGet("", ListRolesAsync)
            .RequireScope(Scope.RoleRead)
            .WithSummary("List the roles defined in a garage")
            .Produces<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK);

        roles
            .MapPost("", CreateRoleAsync)
            .AddEndpointFilter<ValidationFilter<CreateRoleDto>>()
            .RequireScope(Scope.RoleManage)
            .WithSummary("Create a new role in a garage")
            .Produces<RoleDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden);

        roles
            .MapPut("/{roleId}", UpdateRoleAsync)
            .AddEndpointFilter<ValidationFilter<UpdateRoleDto>>()
            .RequireScope(Scope.RoleManage)
            .WithSummary("Update an existing role")
            .Produces<RoleDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        roles
            .MapDelete("/{roleId}", DeleteRoleAsync)
            .RequireScope(Scope.RoleManage)
            .WithSummary("Delete a role")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    internal static async Task<IResult> ListRolesAsync(
        [FromRoute] Guid garageId,
        IRoleService roleService)
    {
        var roles = await roleService.ListByGarageAsync(garageId);
        return Results.Ok(roles.Select(ToDto).ToList());
    }

    internal static async Task<IResult> CreateRoleAsync(
        [FromRoute] Guid garageId,
        CreateRoleDto dto,
        ClaimsPrincipal user,
        ITokenService tokenService,
        IRoleService roleService)
    {
        var actorScopes = tokenService.GetScopesFromClaims(user);
        try
        {
            var role = await roleService.CreateAsync(
                garageId, dto.Name, dto.Description, dto.Scopes, actorScopes);
            return Results.Created($"/api/garages/{garageId}/roles/{role.Id}", ToDto(role));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    internal static async Task<IResult> UpdateRoleAsync(
        [FromRoute] Guid garageId,
        [FromRoute] Guid roleId,
        UpdateRoleDto dto,
        ClaimsPrincipal user,
        ITokenService tokenService,
        IRoleService roleService)
    {
        var actorScopes = tokenService.GetScopesFromClaims(user);
        try
        {
            var role = await roleService.UpdateAsync(
                garageId, roleId, dto.Name, dto.Description, dto.Scopes, actorScopes);
            return Results.Ok(ToDto(role));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            // System-role immutability: editing a platform-seeded role is a conflict, not a bad request.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    internal static async Task<IResult> DeleteRoleAsync(
        [FromRoute] Guid garageId,
        [FromRoute] Guid roleId,
        IRoleService roleService)
    {
        try
        {
            await roleService.DeleteAsync(garageId, roleId);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // System-role immutability or the in-use-delete guard: both are conflicts with current state.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static RoleDto ToDto(RoleEntity role) => new()
    {
        Id = role.Id,
        GarageId = role.GarageId,
        Name = role.Name,
        Description = role.Description,
        Scopes = role.Scopes,
        IsSystem = role.IsSystem,
    };
}
