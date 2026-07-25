namespace Shared.Contracts.DTOs;

/// <summary>
/// Request to create a new garage. Self-serve: any authenticated internal user may create one and
/// automatically becomes its owner (a wildcard-scoped, immutable "Owner" role is seeded for them).
/// </summary>
public class CreateGarageDto
{
    /// <summary>The garage's display name.</summary>
    public required string Name { get; set; }
}
