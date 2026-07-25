namespace MainHub.Api.Models;

// Plain C# class built by hand from raw columns in GarageRepository.Map(reader).
// Mirrors the `garages` table in 001_initial.sql.
public class GarageEntity
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
