namespace Shared.Contracts.DTOs;

public class AdminVehicleListItemDto : VehicleListItemDto
{
  public required Guid OwnerUserId { get; set; }

}
