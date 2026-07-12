namespace MainHub.Api.Models;

// Plain C# class, no BSON attributes anymore. In Mongo, Records used to be
// an array embedded directly inside this document. In Postgres it's a
// separate service_history_records table (FK'd back to this row's Id) - the
// repository reconstructs this Records list by running a SQL JOIN and
// grouping the resulting rows in memory (see ServiceHistoryRepository.ReadGroupedAsync).
public class ServiceHistoryEntity
{
    public required Guid Id { get; set; }
    public required Guid VehicleId { get; set; }
    public required string Title { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime? UpdatedAt { get; set; }
    public required string Description { get; set; }

    // Populated via JOIN, not stored on this table directly - see comment above.
    public required List<ServiceHistoryRecordModel> Records { get; set; } = [];
}
