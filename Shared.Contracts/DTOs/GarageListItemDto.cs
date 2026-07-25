namespace Shared.Contracts.DTOs;

/// <summary>
/// One entry in the list of garages an internal user belongs to, shown when the user chooses which
/// garage to open a session for. Carries the garage identity and the user's role within it.
/// </summary>
public class GarageListItemDto
{
    /// <summary>
    /// The garage id — passed to the garage-session endpoint to mint a garage-scoped token.
    /// </summary>
    public required Guid GarageId { get; set; }

    /// <summary>
    /// The garage's display name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The name of the role the user holds in this garage (e.g. "Owner", "Mechanic").
    /// </summary>
    public required string RoleName { get; set; }
}
