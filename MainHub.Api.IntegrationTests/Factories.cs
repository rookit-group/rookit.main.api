using MainHub.Api.Models;
using Shared.Contracts.Enums;

namespace MainHub.Api.IntegrationTests;

// Tiny factory helpers so tests don't need to fill 15 required fields just to
// exercise one behavior. Each helper takes only the fields a test typically
// cares about and picks sensible defaults for everything else. Kept here in
// the test project so it can't leak into production code.
internal static class Factories
{
    // Use a fixed base timestamp so equality checks aren't racing DateTime.UtcNow.
    // AddTicks(0) below just makes it a distinct call site if we ever need to nudge.
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static UserEntity User(
        Guid? id = null,
        string name = "Test User",
        string? email = "test@example.com",
        string? providerId = null,
        string? phone = "+1234567890",
        string? pictureUrl = "https://example.com/p.png",
        DateTime? createdAt = null,
        DateTime? updatedAt = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Email = email,
            ProviderId = providerId ?? $"provider-{Guid.NewGuid():N}",
            Phone = phone,
            PictureUrl = pictureUrl,
            CreatedAt = createdAt ?? BaseUtc,
            UpdatedAt = updatedAt,
        };

    public static InternalUserProfileEntity InternalUserProfile(
        Guid userId,
        Guid? id = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId,
            CreatedAt = createdAt ?? BaseUtc,
            UpdatedAt = updatedAt,
        };

    public static VehicleEntity Vehicle(
        Guid internalUserProfileId,
        Guid? id = null,
        string licensePlate = "AA-123-BB",
        string vin = "1HGBH41JXMN109186",
        string brand = "Toyota",
        string model = "Corolla",
        int year = 2020,
        DateTime? boughtAt = null,
        WheelDriveType wheelDriveType = WheelDriveType.FWD,
        int engineCapacity = 1600,
        FuelType fuelType = FuelType.Gasoline,
        TransmissionType transmissionType = TransmissionType.Automatic,
        int enginePower = 132,
        string color = "Red",
        int mileage = 12000,
        List<string>? photoStorageKeys = null,
        DateTime? createdAt = null,
        DateTime? updatedAt = null) => new()
        {
            Id = id ?? Guid.NewGuid(),
            InternalUserProfileId = internalUserProfileId,
            LicensePlate = licensePlate,
            Vin = vin,
            Brand = brand,
            Model = model,
            Year = year,
            BoughtAt = boughtAt ?? BaseUtc.AddDays(-30),
            WheelDriveType = wheelDriveType,
            EngineCapacity = engineCapacity,
            FuelType = fuelType,
            TransmissionType = transmissionType,
            EnginePower = enginePower,
            Color = color,
            Mileage = mileage,
            PhotoStorageKeys = photoStorageKeys,
            CreatedAt = createdAt ?? BaseUtc,
            UpdatedAt = updatedAt,
        };

    public static ServiceHistoryEntity ServiceHistory(
        Guid vehicleId,
        Guid? id = null,
        string title = "Oil change",
        string description = "Routine maintenance",
        DateTime? createdAt = null,
        DateTime? updatedAt = null,
        List<ServiceHistoryRecordModel>? records = null)
    {
        var shId = id ?? Guid.NewGuid();
        return new ServiceHistoryEntity
        {
            Id = shId,
            VehicleId = vehicleId,
            Title = title,
            Description = description,
            CreatedAt = createdAt ?? BaseUtc,
            UpdatedAt = updatedAt,
            Records = records ?? [],
        };
    }

    public static ServiceHistoryRecordModel Record(
        Guid? id = null,
        Guid? serviceHistoryId = null,
        string title = "Oil filter",
        string description = "Replaced oil filter",
        int price = 50) => new()
        {
            Id = id ?? Guid.NewGuid(),
            ServiceHistoryId = serviceHistoryId ?? Guid.Empty,
            Title = title,
            Description = description,
            Price = price,
        };

    public static RefreshTokenEntity RefreshToken(
        Guid userId,
        Guid? id = null,
        string? token = null,
        string providerId = "provider-1",
        DateTime? expiresAt = null,
        DateTime? createdAt = null,
        bool isRevoked = false) => new()
        {
            Id = id ?? Guid.NewGuid(),
            Token = token ?? $"tok-{Guid.NewGuid():N}",
            UserId = userId,
            ProviderId = providerId,
            ExpiresAt = expiresAt ?? BaseUtc.AddDays(30),
            CreatedAt = createdAt ?? BaseUtc,
            IsRevoked = isRevoked,
        };
}
