namespace Shared.Contracts.DTOs;

/// <summary>
/// Request to update a garage's details. Requires the <c>garage:manage</c> scope.
/// </summary>
public class UpdateGarageDto
{
    /// <summary>The garage's new display name.</summary>
    public required string Name { get; set; }
}
