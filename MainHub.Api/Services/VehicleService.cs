using MainHub.Api.Repositories;
using MainHub.Api.DTOs;
using MainHub.Api.Models;
using MainHub.Api.Shared;

namespace MainHub.Api.Services;

public interface IVehicleService
{
    Task<VehicleDto> GetVehicleByIdAsync(Guid vehicleId, Guid userId);
    Task<VehicleDto> GetVehicleByIdAsync(Guid vehicleId);
    Task UpdateAsync(Guid vehicleId, UpdateVehicleDto updateVehicleDto, Guid userId);
    Task CreateAsync(CreateVehicleDto createVehicleDto, Guid userId);
    Task<List<VehicleListItemDto>> GetAllByUserAsync(Guid userId);
    Task DeleteAsync(Guid vehicleId, Guid userId);
    Task<PagedResultDto<AdminVehicleListItemDto>> GetAllVehiclesAsync(int page, int pageSize);
    Task<string> UploadPhotoAsync(Stream photo, long size, string contentType, Guid userId);
}

// No more MongoDB.Driver / FilterDefinition / UpdateDefinition here -
// ownership checks and partial updates are now plain repository calls backed
// by SQL (see VehicleRepository.BelongsToUserAsync/UpdateAsync).
public class VehicleService(
  IVehicleRepository repository,
  IUserService userService,
  IServiceHistoryService serviceHistoryService,
  IMinioService minioService
) : IVehicleService
{
    private readonly IVehicleRepository _repository = repository;
    private readonly IUserService _userService = userService;
    private readonly IServiceHistoryService _serviceHistoryService = serviceHistoryService;
    private readonly IMinioService _minioService = minioService;

    // BelongsToUserAsync replaces what used to be checking user.VehicleIds.Contains(vehicleId).
    public async Task<VehicleDto> GetVehicleByIdAsync(Guid vehicleId, Guid userId)
    {
        if (!await _repository.BelongsToUserAsync(vehicleId, userId))
        {
            throw new UnauthorizedAccessException("You don't have permission to access this vehicle.");
        }

        var vehicle = await _repository.GetByIdAsync(vehicleId);
        if (vehicle == null)
        {
            throw new ArgumentException($"Vehicle with ID {vehicleId} not found.");
        }

        return await MapVehicleDtoAsync(vehicle);
    }

    public async Task<VehicleDto> GetVehicleByIdAsync(Guid vehicleId)
    {
        var vehicle = await _repository.GetByIdAsync(vehicleId);
        if (vehicle == null)
        {
            throw new ArgumentException($"Vehicle with ID {vehicleId} not found.");
        }

        return await MapVehicleDtoAsync(vehicle);
    }

    public async Task UpdateAsync(Guid vehicleId, UpdateVehicleDto updateVehicleDto, Guid userId)
    {
        if (!await _repository.BelongsToUserAsync(vehicleId, userId))
        {
            throw new UnauthorizedAccessException("You don't have permission to update this vehicle.");
        }

        // Storage keys arrive from the client, so we can't blindly trust them:
        // enforce the per-user prefix and confirm the object actually exists in
        // Minio before persisting the pointer. Old photo objects, if any, are
        // left in place - a scheduled GC job sweeps unreferenced ones later.
        if (updateVehicleDto.PhotoStorageKey is not null)
        {
            await ValidatePhotoStorageKeyAsync(updateVehicleDto.PhotoStorageKey, userId);
        }

        var updated = await _repository.UpdateAsync(vehicleId, updateVehicleDto, DateTime.UtcNow);
        if (!updated)
        {
            throw new ArgumentException($"Vehicle with ID {vehicleId} not found.");
        }
    }

    public async Task DeleteAsync(Guid vehicleId, Guid userId)
    {
        if (!await _repository.BelongsToUserAsync(vehicleId, userId))
        {
            throw new ArgumentException("Vehicle not associated with the user.", nameof(vehicleId));
        }

        await _serviceHistoryService.DeleteAllByVehicleIdAsync(vehicleId, userId);
        await _repository.DeleteAsync(vehicleId);
    }

    public async Task<List<VehicleListItemDto>> GetAllByUserAsync(Guid userId)
    {
        var user = await _userService.GetByIdAsync(userId);
        if (user == null)
        {
            throw new ArgumentException("User not found.", nameof(userId));
        }

        var vehicleEntities = await _repository.GetAllByUserAsync(userId);
        var items = await Task.WhenAll(vehicleEntities.Select(async v => new VehicleListItemDto
        {
            Id = v.Id,
            LicensePlate = v.LicensePlate,
            Brand = v.Brand,
            Model = v.Model,
            Year = v.Year,
            PhotoUrl = await ResolvePhotoUrlAsync(v.PhotoStorageKey),
        }));
        return items.ToList();
    }

    public async Task CreateAsync(CreateVehicleDto createVehicleDto, Guid userId)
    {
        if (createVehicleDto.PhotoStorageKey is not null)
        {
            await ValidatePhotoStorageKeyAsync(createVehicleDto.PhotoStorageKey, userId);
        }

        var vehicle = new VehicleEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Vin = createVehicleDto.Vin,
            Model = createVehicleDto.Model,
            UpdatedAt = null,
            CreatedAt = DateTime.UtcNow,
            BoughtAt = createVehicleDto.BoughtAt,
            Color = createVehicleDto.Color,
            LicensePlate = createVehicleDto.LicensePlate,
            Brand = createVehicleDto.Brand,
            EngineCapacity = createVehicleDto.EngineCapacity,
            EnginePower = createVehicleDto.EnginePower,
            FuelType = createVehicleDto.FuelType,
            TransmissionType = createVehicleDto.TransmissionType,
            Mileage = createVehicleDto.Mileage,
            WheelDriveType = createVehicleDto.WheelDriveType,
            Year = createVehicleDto.Year,
            PhotoStorageKey = createVehicleDto.PhotoStorageKey,
        };

        await _repository.CreateAsync(vehicle);
    }

    public async Task<PagedResultDto<AdminVehicleListItemDto>> GetAllVehiclesAsync(int page, int pageSize)
    {
        var (skip, limit) = PaginationHelper.Normalize(page, pageSize);

        var vehicles = await _repository.GetAllVehiclesAsync(skip, limit);
        var totalItems = (int)await _repository.GetCountAsync();
        var ownerMap = await _userService.GetOwnerMapByVehicleIdsAsync(vehicles.Select(v => v.Id).ToList());
        var items = (await Task.WhenAll(vehicles.Select(v => MapAdminVehicleListItemAsync(v, ownerMap)))).ToList();

        return new PagedResultDto<AdminVehicleListItemDto>
        {
            Items = items,
            TotalItems = totalItems,
        };
    }

    // Uploads the raw bytes to Minio under a fresh, per-user, opaque key. The
    // key is generated server-side so the client can't influence where the
    // object lands, and the userId prefix lets Create/Update later prove the
    // caller actually owns the key they're passing back.
    public async Task<string> UploadPhotoAsync(Stream photo, long size, string contentType, Guid userId)
    {
        var storageKey = BuildPhotoStorageKey(userId);
        await _minioService.UploadAsync(photo, size, storageKey, contentType);
        return storageKey;
    }

    private static string BuildPhotoStorageKey(Guid userId) =>
        $"vehicles/photos/{userId}/{Guid.NewGuid():N}";

    // Two guarantees, in order:
    //   1. The key sits under this user's prefix - prevents a client from
    //      pasting someone else's key into their own CreateVehicleDto.
    //   2. An object actually exists at that key - prevents saving pointers
    //      to nothing (e.g. client fakes a UUID that was never uploaded).
    private async Task ValidatePhotoStorageKeyAsync(string storageKey, Guid userId)
    {
        var expectedPrefix = $"vehicles/photos/{userId}/";
        if (!storageKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Photo storage key does not belong to the current user.");
        }

        if (!await _minioService.ExistsAsync(storageKey))
        {
            throw new ArgumentException("Photo storage key does not reference an existing upload.");
        }
    }

    private async Task<string?> ResolvePhotoUrlAsync(string? storageKey) =>
        string.IsNullOrEmpty(storageKey) ? null : await _minioService.GetPresignedGetUrlAsync(storageKey);

    private async Task<VehicleDto> MapVehicleDtoAsync(VehicleEntity vehicle) => new()
    {
        Id = vehicle.Id,
        Vin = vehicle.Vin,
        Brand = vehicle.Brand,
        Model = vehicle.Model,
        Year = vehicle.Year,
        Color = vehicle.Color,
        LicensePlate = vehicle.LicensePlate,
        BoughtAt = vehicle.BoughtAt,
        EngineCapacity = vehicle.EngineCapacity,
        EnginePower = vehicle.EnginePower,
        FuelType = vehicle.FuelType,
        TransmissionType = vehicle.TransmissionType,
        WheelDriveType = vehicle.WheelDriveType,
        Mileage = vehicle.Mileage,
        PhotoUrl = await ResolvePhotoUrlAsync(vehicle.PhotoStorageKey),
        CreatedAt = vehicle.CreatedAt,
        UpdatedAt = vehicle.UpdatedAt,
    };

    private async Task<AdminVehicleListItemDto> MapAdminVehicleListItemAsync(
      VehicleEntity vehicle,
      IReadOnlyDictionary<Guid, Guid> ownerMap
    )
    {
        if (!ownerMap.TryGetValue(vehicle.Id, out var ownerUserId))
        {
            throw new InvalidOperationException(
              $"Vehicle {vehicle.Id} has no assigned owner."
            );
        }

        return new AdminVehicleListItemDto()
        {
            Id = vehicle.Id,
            LicensePlate = vehicle.LicensePlate,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Year = vehicle.Year,
            PhotoUrl = await ResolvePhotoUrlAsync(vehicle.PhotoStorageKey),
            OwnerUserId = ownerUserId,
        };
    }
}
