namespace MainHub.Api.Models;

public class ServiceHistoryRecordModel
{
    public required Guid Id { get; set; }
    public Guid ServiceHistoryId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required int Price { get; set; }
}
