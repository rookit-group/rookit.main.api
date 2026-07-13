namespace MainHub.Api.DTOs;

/// <summary>
/// Response returned by the vehicle photo upload endpoint. The client stores
/// this key and sends it back inside CreateVehicleDto / UpdateVehicleDto to
/// attach the uploaded photo to a vehicle.
/// </summary>
public class UploadVehiclePhotoResponseDto
{
    /// <summary>
    /// Opaque storage key identifying the uploaded object in Minio. Only
    /// meaningful when handed straight back to the API - the client should
    /// never construct it itself.
    /// </summary>
    public required string StorageKey { get; set; }
}
