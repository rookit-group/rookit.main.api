using MainHub.Api.Repositories;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class GarageRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly GarageRepository _sut;

    public GarageRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _sut = new GarageRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // CreateAsync + GetByIdAsync also proves Map(reader) reads every column
    // back correctly, which is the single "mapping tested once" for GarageEntity.
    [Fact]
    public async Task Create_then_GetById_roundtrips_every_column()
    {
        var garage = Factories.Garage(
            name: "Downtown Motors",
            updatedAt: new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc));

        await _sut.CreateAsync(garage);
        var fetched = await _sut.GetByIdAsync(garage.Id);

        Assert.NotNull(fetched);
        Assert.Equal(garage.Id, fetched!.Id);
        Assert.Equal(garage.Name, fetched.Name);
        Assert.Equal(garage.CreatedAt, fetched.CreatedAt);
        Assert.Equal(garage.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Create_persists_null_updated_at()
    {
        var garage = Factories.Garage(updatedAt: null);

        await _sut.CreateAsync(garage);
        var fetched = await _sut.GetByIdAsync(garage.Id);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.UpdatedAt);
    }

    [Fact]
    public async Task GetById_returns_null_when_missing()
    {
        var missing = await _sut.GetByIdAsync(Guid.NewGuid());
        Assert.Null(missing);
    }
}
