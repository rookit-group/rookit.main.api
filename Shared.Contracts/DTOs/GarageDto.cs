namespace Shared.Contracts.DTOs;

/// <summary>
/// A garage's core details. Returned after a garage is updated.
/// </summary>
public class GarageDto
{
    /// <summary>The garage's id.</summary>
    public required Guid Id { get; set; }

    /// <summary>The garage's display name.</summary>
    public required string Name { get; set; }
}
