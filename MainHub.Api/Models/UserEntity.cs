namespace MainHub.Api.Models;

// Plain C# class, no BSON attributes anymore - Npgsql doesn't map rows to
// objects automatically (see UserRepository.Map for the manual mapping).
//
// Note: the old Mongo document had a VehicleIds array embedded here. That's
// gone - a user's vehicles are now found by querying the vehicles table
// WHERE user_id = this user's Id (see IVehicleRepository.GetAllByUserAsync),
// the same shift from "array of foreign ids on the parent" to "foreign key
// on the child row" as with VehicleEntity.UserId.
public class UserEntity
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    public required string ProviderId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime? UpdatedAt { get; set; }
    public required string? Phone { get; set; }
    public required string? PictureUrl { get; set; }
}
