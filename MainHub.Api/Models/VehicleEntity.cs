using Shared.Contracts.Enums;

namespace MainHub.Api.Models;

// Plain C# class, no BSON attributes anymore - Npgsql doesn't map rows to
// objects automatically, so each repository's Map(reader) method builds this
// by hand from the raw columns (see VehicleRepository.cs).
public class VehicleEntity
{
    public required Guid Id { get; set; }

    // Replaces the old UserEntity.VehicleIds array. Ownership is now a
    // foreign key living on the "many" side (a vehicle points at one owner),
    // matching the vehicles.user_id column in db/init.sql. Null means
    // unowned/unattached, same idea as an unset field in Mongo.
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
    public List<string>? PhotoStorageKeys { get; set; } = null;
}
