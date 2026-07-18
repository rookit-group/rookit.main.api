using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Shared.Contracts.DTOs;
using MainHub.Api.Shared;

namespace MainHub.Api.Services;

public interface IUserService
{
    Task UpdateAsync(UpdateUserDto userDto, Guid userId);
    Task<UserEntity?> GetUserByProviderIdAsync(string providerId);
    Task<GetMeDto> GetMeByUserIdAsync(Guid userId);
    Task<List<UserEntity>> GetAllAsync();
    Task<UserEntity?> GetByIdAsync(Guid id);
    Task<UserEntity> CreateAsync(string name, string? email, string providerId, string? phone, string? pictureUrl);
    Task DeleteAsync(Guid id);
    Task<Dictionary<Guid, Guid>> GetOwnerMapByVehicleIdsAsync(IReadOnlyCollection<Guid> vehicleIds);
    Task<PagedResultDto<AdminUserListItemDto>> GetAllUsersAsync(int page, int pageSize);
}

public class UserService(
    IUserRepository repository,
    IExternalUserProfileRepository externalUserProfileRepository,
    IVehicleRepository vehicleRepository
) : IUserService
{
    private readonly IUserRepository _repository = repository;
    private readonly IExternalUserProfileRepository _externalUserProfileRepository = externalUserProfileRepository;
    private readonly IVehicleRepository _vehicleRepository = vehicleRepository;

    public async Task UpdateAsync(UpdateUserDto userDto, Guid userId)
    {
        var name = string.IsNullOrWhiteSpace(userDto.Name) ? null : userDto.Name;
        var email = string.IsNullOrWhiteSpace(userDto.Email) ? null : userDto.Email;

        var updated = await _repository.UpdateProfileAsync(userId, name, email, DateTime.UtcNow);
        if (!updated)
        {
            throw new KeyNotFoundException($"User with ID {userId} not found.");
        }
    }

    public async Task<UserEntity?> GetUserByProviderIdAsync(string providerId)
    {
        return await _repository.GetByProviderIdAsync(providerId);
    }

    public async Task<GetMeDto> GetMeByUserIdAsync(Guid userId)
    {
        var user = await _repository.GetByIdAsync(userId);
        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        return new GetMeDto
        {
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            PictureUrl = user.PictureUrl,
            UpdatedAt = user.UpdatedAt,
        };
    }

    public async Task<List<UserEntity>> GetAllAsync() => await _repository.GetAllAsync();

    public async Task<UserEntity?> GetByIdAsync(Guid id) => await _repository.GetByIdAsync(id);

    public async Task<UserEntity> CreateAsync(
      string name,
      string? email,
      string providerId,
      string? phone,
      string? pictureUrl
    )
    {
        var now = DateTime.UtcNow;
        var userEntity = new UserEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            ProviderId = providerId,
            CreatedAt = now,
            UpdatedAt = null,
            Phone = phone,
            PictureUrl = pictureUrl,
        };

        await _repository.CreateAsync(userEntity);

        await _externalUserProfileRepository.CreateAsync(new ExternalUserProfileEntity
        {
            Id = Guid.NewGuid(),
            UserId = userEntity.Id,
            CreatedAt = now,
            UpdatedAt = null,
        });

        return userEntity;
    }

    public async Task DeleteAsync(Guid id) => await _repository.DeleteAsync(id);

    public async Task<Dictionary<Guid, Guid>> GetOwnerMapByVehicleIdsAsync(
      IReadOnlyCollection<Guid> vehicleIds
    ) => await _vehicleRepository.GetOwnerMapAsync(vehicleIds);

    public async Task<PagedResultDto<AdminUserListItemDto>> GetAllUsersAsync(
      int page,
      int pageSize
    )
    {
        var (skip, limit) = PaginationHelper.Normalize(page, pageSize);

        var items = await _repository.GetAllUsersPagedAsync(skip, limit);
        var totalItems = (int)await _repository.CountAllUsersAsync();

        return new PagedResultDto<AdminUserListItemDto>
        {
            Items = items.Select(d => new AdminUserListItemDto
            {
                Id = d.Id,
                Name = d.Name,
                Email = d.Email,
                Phone = d.Phone,
                PictureUrl = d.PictureUrl,
                ProviderId = d.ProviderId,
                CreatedAt = d.CreatedAt,
            }).ToList(),
            TotalItems = totalItems,
        };
    }
}

