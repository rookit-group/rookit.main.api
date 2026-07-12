namespace MainHub.Api.Models;

// This used to be embedded inline inside a ServiceHistoryEntity's Records
// array (a Mongo sub-document). Now it's its own row in the
// service_history_records table, so it needs its own Id and a
// ServiceHistoryId foreign key to know which parent it belongs to - that
// link was implicit before (just being nested in the same document) and is
// explicit now.
public class ServiceHistoryRecordModel
{
    public required Guid Id { get; set; }
    public Guid ServiceHistoryId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required int Price { get; set; }
}
