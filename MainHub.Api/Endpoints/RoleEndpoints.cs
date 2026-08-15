using MainHub.Api.Authorization;
using MainHub.Api.Models;
using MainHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Endpoints;

// Garage-scoped role listing. The route runs under a GarageJwt whose garage matches the route's
// {garageId} and whose scopes grant role:read (RequireScope). This handler only reads roles and
// maps them to DTOs.
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
    }

    internal static async Task<IResult> ListRolesAsync(
        [FromRoute] Guid garageId,
        IRoleService roleService)
    {
        var roles = await roleService.ListByGarageAsync(garageId);
        return Results.Ok(roles.Select(ToDto).ToList());
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
