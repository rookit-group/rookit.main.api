using MainHub.Api.Shared;

namespace MainHub.Api.Models;

public class VehicleEntity
{
    public required Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public required string LicensePlate { get; set; }
    public required string Vin { get; set; }
    public required string Brand { get; set; }
    public required string Model { get; set; }
    public required int Year { get; set; }
    public required DateTime? BoughtAt { get; set; } = null;
    public required WheelDriveType WheelDriveType { get; set; }
    public required int EngineCapacity { get; set; }
    public required FuelType FuelType { get; set; }
    public required DateTime? UpdatedAt { get; set; } = null;
    public required DateTime CreatedAt { get; set; }
    public required int EnginePower { get; set; }
    public required string Color { get; set; }
    public required TransmissionType TransmissionType { get; set; }
    public required int Mileage { get; set; }
    public string? PhotoUrl { get; set; } = null;
}
