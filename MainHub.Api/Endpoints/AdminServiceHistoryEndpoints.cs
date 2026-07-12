using MainHub.Api.DTOs;
using MainHub.Api.DTOs.ServiceHistory;
using MainHub.Api.Services;
using MainHub.Api.Enums;

namespace MainHub.Api.Endpoints;

public static partial class AdminServiceHistoryEndpoints
{
  public static void MapAdminServiceHistoryEndpoints(this IEndpointRouteBuilder app)
  {
    var admin = app
      .MapGroup("/api/admin")
      .WithTags("Admin")
      .RequireAuthorization(nameof(AuthPolicy.RequireAdminJwt));

    admin
      .MapGet("/vehicles/{vehicleId}/service-history", GetAdminVehicleHistoryAsync)
      .WithSummary("Get service history visits by vehicle id for admin")
      .Produces<List<AdminServiceHistoryVisitDto>>(StatusCodes.Status200OK);

    admin
      .MapGet("/service-history/{serviceHistoryId}/records", GetAdminServiceRecordsAsync)
      .WithSummary("Get service records by service history id for admin")
      .Produces<List<ServiceHistoryRecordDto>>(StatusCodes.Status200OK);
  }

  internal static async Task<IResult> GetAdminVehicleHistoryAsync(
    Guid vehicleId,
    IServiceHistoryService serviceHistoryService
  )
  {
    var visits = await serviceHistoryService.GetAdminVisitsByVehicleIdAsync(vehicleId);
    return Results.Ok(visits);
  }

  internal static async Task<IResult> GetAdminServiceRecordsAsync(
    Guid serviceHistoryId,
    IServiceHistoryService serviceHistoryService
  )
  {
    try
    {
      var records = await serviceHistoryService.GetAdminRecordsByServiceHistoryIdAsync(serviceHistoryId);
      return Results.Ok(records);
    }
    catch (KeyNotFoundException)
    {
      return Results.NotFound();
    }
  }
}
