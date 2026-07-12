using MainHub.Api.Repositories;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class UserRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly UserRepository _sut;

    public UserRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _sut = new UserRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // CreateAsync + GetByIdAsync also proves Map(reader) reads every column
    // back correctly, which is the single "mapping tested once" per entity.
    [Fact]
    public async Task Create_then_GetById_roundtrips_every_column()
    {
        var user = Factories.User(
            name: "Alice",
            email: "alice@example.com",
            providerId: "tg-42",
            phone: "+1-555-0100",
            pictureUrl: "https://example.com/alice.png",
            updatedAt: new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc));

        await _sut.CreateAsync(user);
        var fetched = await _sut.GetByIdAsync(user.Id);

        Assert.NotNull(fetched);
        Assert.Equal(user.Id, fetched!.Id);
        Assert.Equal(user.Name, fetched.Name);
        Assert.Equal(user.Email, fetched.Email);
        Assert.Equal(user.ProviderId, fetched.ProviderId);
        Assert.Equal(user.Phone, fetched.Phone);
        Assert.Equal(user.PictureUrl, fetched.PictureUrl);
        Assert.Equal(user.CreatedAt, fetched.CreatedAt);
        Assert.Equal(user.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Create_persists_nullable_columns_as_null()
    {
        var user = Factories.User(email: null, phone: null, pictureUrl: null, updatedAt: null);

        await _sut.CreateAsync(user);
        var fetched = await _sut.GetByIdAsync(user.Id);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.Email);
        Assert.Null(fetched.Phone);
        Assert.Null(fetched.PictureUrl);
        Assert.Null(fetched.UpdatedAt);
    }

    [Fact]
    public async Task GetById_returns_null_when_missing()
    {
        var missing = await _sut.GetByIdAsync(Guid.NewGuid());
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetByProviderId_returns_matching_user()
    {
        var a = Factories.User(providerId: "tg-a");
        var b = Factories.User(providerId: "tg-b");
        await _sut.CreateAsync(a);
        await _sut.CreateAsync(b);

        var fetched = await _sut.GetByProviderIdAsync("tg-b");

        Assert.NotNull(fetched);
        Assert.Equal(b.Id, fetched!.Id);
    }

    [Fact]
    public async Task GetByProviderId_returns_null_when_missing()
    {
        var fetched = await _sut.GetByProviderIdAsync("nope");
        Assert.Null(fetched);
    }

    [Fact]
    public async Task GetAll_returns_all_users()
    {
        await _sut.CreateAsync(Factories.User(providerId: "p1"));
        await _sut.CreateAsync(Factories.User(providerId: "p2"));
        await _sut.CreateAsync(Factories.User(providerId: "p3"));

        var all = await _sut.GetAllAsync();

        Assert.Equal(3, all.Count);
    }

    // ORDER BY name DESC + OFFSET/LIMIT.
    [Fact]
    public async Task GetAllUsersPaged_orders_by_name_desc_and_applies_skip_limit()
    {
        await _sut.CreateAsync(Factories.User(name: "Alice", providerId: "p1"));
        await _sut.CreateAsync(Factories.User(name: "Bob", providerId: "p2"));
        await _sut.CreateAsync(Factories.User(name: "Charlie", providerId: "p3"));
        await _sut.CreateAsync(Factories.User(name: "Dave", providerId: "p4"));

        var page = await _sut.GetAllUsersPagedAsync(skip: 1, limit: 2);

        Assert.Equal(2, page.Count);
        // ordered desc: Dave, Charlie, Bob, Alice; skip 1 -> Charlie, Bob.
        Assert.Equal("Charlie", page[0].Name);
        Assert.Equal("Bob", page[1].Name);
    }

    [Fact]
    public async Task CountAllUsers_returns_row_count()
    {
        Assert.Equal(0, await _sut.CountAllUsersAsync());
        await _sut.CreateAsync(Factories.User(providerId: "p1"));
        await _sut.CreateAsync(Factories.User(providerId: "p2"));
        Assert.Equal(2, await _sut.CountAllUsersAsync());
    }

    [Fact]
    public async Task Delete_removes_user()
    {
        var user = Factories.User();
        await _sut.CreateAsync(user);

        await _sut.DeleteAsync(user.Id);

        Assert.Null(await _sut.GetByIdAsync(user.Id));
    }

    [Fact]
    public async Task Delete_missing_id_is_noop()
    {
        // Should not throw.
        await _sut.DeleteAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task UpdateProfile_updates_name_and_email_and_bumps_updated_at()
    {
        var user = Factories.User(name: "Old", email: "old@example.com", updatedAt: null);
        await _sut.CreateAsync(user);

        var newUpdatedAt = new DateTime(2026, 3, 3, 3, 3, 3, DateTimeKind.Utc);
        var updated = await _sut.UpdateProfileAsync(user.Id, "New", "new@example.com", newUpdatedAt);

        Assert.True(updated);
        var fetched = await _sut.GetByIdAsync(user.Id);
        Assert.Equal("New", fetched!.Name);
        Assert.Equal("new@example.com", fetched.Email);
        Assert.Equal(newUpdatedAt, fetched.UpdatedAt);
    }

    // Null args must preserve the existing column via COALESCE, not overwrite it.
    [Fact]
    public async Task UpdateProfile_null_args_preserve_existing_values()
    {
        var user = Factories.User(name: "Keep", email: "keep@example.com");
        await _sut.CreateAsync(user);

        var updated = await _sut.UpdateProfileAsync(user.Id, name: null, email: null, updatedAt: DateTime.UtcNow);

        Assert.True(updated);
        var fetched = await _sut.GetByIdAsync(user.Id);
        Assert.Equal("Keep", fetched!.Name);
        Assert.Equal("keep@example.com", fetched.Email);
    }

    [Fact]
    public async Task UpdateProfile_missing_id_returns_false()
    {
        var updated = await _sut.UpdateProfileAsync(Guid.NewGuid(), "x", "y@z", DateTime.UtcNow);
        Assert.False(updated);
    }
}
