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
}

public class VehicleService(
  IVehicleRepository repository,
  IUserService userService,
  IServiceHistoryService serviceHistoryService
) : IVehicleService
{
    private readonly IVehicleRepository _repository = repository;
    private readonly IUserService _userService = userService;
    private readonly IServiceHistoryService _serviceHistoryService = serviceHistoryService;

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

        return MapVehicleDto(vehicle);
    }

    public async Task<VehicleDto> GetVehicleByIdAsync(Guid vehicleId)
    {
        var vehicle = await _repository.GetByIdAsync(vehicleId);
        if (vehicle == null)
        {
            throw new ArgumentException($"Vehicle with ID {vehicleId} not found.");
        }

        return MapVehicleDto(vehicle);
    }

    public async Task UpdateAsync(Guid vehicleId, UpdateVehicleDto updateVehicleDto, Guid userId)
    {
        if (!await _repository.BelongsToUserAsync(vehicleId, userId))
        {
            throw new UnauthorizedAccessException("You don't have permission to update this vehicle.");
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
        return vehicleEntities.Select(v => new VehicleListItemDto
        {
            Id = v.Id,
            LicensePlate = v.LicensePlate,
            Brand = v.Brand,
            Model = v.Model,
            Year = v.Year,
            PhotoUrl = v.PhotoUrl,
        }).ToList();
    }

    public async Task CreateAsync(CreateVehicleDto createVehicleDto, Guid userId)
    {
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
            PhotoUrl = null,
        };

        await _repository.CreateAsync(vehicle);
    }

    public async Task<PagedResultDto<AdminVehicleListItemDto>> GetAllVehiclesAsync(int page, int pageSize)
    {
        var (skip, limit) = PaginationHelper.Normalize(page, pageSize);

        var vehicles = await _repository.GetAllVehiclesAsync(skip, limit);
        var totalItems = (int)await _repository.GetCountAsync();
        var ownerMap = await _userService.GetOwnerMapByVehicleIdsAsync(vehicles.Select(v => v.Id).ToList());
        var items = vehicles.Select(v => MapAdminVehicleListItem(v, ownerMap)).ToList();

        return new PagedResultDto<AdminVehicleListItemDto>
        {
            Items = items,
            TotalItems = totalItems,
        };
    }

    private static VehicleDto MapVehicleDto(VehicleEntity vehicle) => new()
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
        PhotoUrl = vehicle.PhotoUrl,
        CreatedAt = vehicle.CreatedAt,
        UpdatedAt = vehicle.UpdatedAt,
    };

    private static AdminVehicleListItemDto MapAdminVehicleListItem(
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
            PhotoUrl = vehicle.PhotoUrl,
            OwnerUserId = ownerUserId,
        };
    }
}
