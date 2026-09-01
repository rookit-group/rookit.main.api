using MainHub.Api.Models;
using MainHub.Api.Repositories;
using Npgsql;
using Xunit;

namespace MainHub.Api.IntegrationTests;

[Collection("Postgres")]
public class InvitationRepositoryTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly GarageRepository _garages;
    private readonly RoleRepository _roles;
    private readonly InvitationRepository _sut;

    public InvitationRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _garages = new GarageRepository(fixture.DataSource);
        _roles = new RoleRepository(fixture.DataSource);
        _sut = new InvitationRepository(fixture.DataSource);
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // Creates a garage + role and returns their ids - the parent rows an invitation needs.
    private async Task<(Guid garageId, Guid roleId)> SeedGarageAndRoleAsync(string roleName = "Mechanic")
    {
        var garage = Factories.Garage();
        await _garages.CreateAsync(garage);
        var role = Factories.Role(garage.Id, name: roleName);
        await _roles.CreateAsync(role);
        return (garage.Id, role.Id);
    }

    // AddAsync + GetByIdAsync proves Map(reader) reads every column back correctly.
    [Fact]
    public async Task Add_then_GetById_roundtrips_every_column()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        var invitation = Factories.Invitation(
            garageId, roleId,
            phone: "+15551234",
            updatedAt: new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc));

        await _sut.AddAsync(invitation);
        var fetched = await _sut.GetByIdAsync(invitation.Id);

        Assert.NotNull(fetched);
        Assert.Equal(invitation.Id, fetched!.Id);
        Assert.Equal(garageId, fetched.GarageId);
        Assert.Equal("+15551234", fetched.Phone);
        Assert.Equal(roleId, fetched.RoleId);
        Assert.Equal(invitation.CreatedAt, fetched.CreatedAt);
        Assert.Equal(invitation.UpdatedAt, fetched.UpdatedAt);
    }

    [Fact]
    public async Task Add_persists_null_updated_at()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        await _sut.AddAsync(Factories.Invitation(garageId, roleId, updatedAt: null));

        var fetched = await _sut.GetByIdAsync((await _sut.ListByGarageAsync(garageId)).Single().Id);

        Assert.NotNull(fetched);
        Assert.Null(fetched!.UpdatedAt);
    }

    [Fact]
    public async Task GetById_returns_null_when_missing()
    {
        Assert.Null(await _sut.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Add_rejects_a_duplicate_phone_in_the_same_garage()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        await _sut.AddAsync(Factories.Invitation(garageId, roleId, phone: "+15550000"));

        // UNIQUE (garage_id, phone) rejects a second pending invitation for the same phone.
        await Assert.ThrowsAsync<PostgresException>(
            () => _sut.AddAsync(Factories.Invitation(garageId, roleId, phone: "+15550000")));
    }

    [Fact]
    public async Task Add_allows_the_same_phone_in_a_different_garage()
    {
        var (garageA, roleA) = await SeedGarageAndRoleAsync();
        var (garageB, roleB) = await SeedGarageAndRoleAsync();

        await _sut.AddAsync(Factories.Invitation(garageA, roleA, phone: "+15551111"));
        await _sut.AddAsync(Factories.Invitation(garageB, roleB, phone: "+15551111"));

        Assert.Single(await _sut.ListByGarageAsync(garageA));
        Assert.Single(await _sut.ListByGarageAsync(garageB));
    }

    [Fact]
    public async Task Add_rejects_a_role_from_a_different_garage()
    {
        var (garageId, _) = await SeedGarageAndRoleAsync();
        var (_, foreignRoleId) = await SeedGarageAndRoleAsync("Foreign");

        // Composite FK (role_id, garage_id) -> roles(id, garage_id) blocks a cross-garage role.
        await Assert.ThrowsAsync<PostgresException>(
            () => _sut.AddAsync(Factories.Invitation(garageId, foreignRoleId)));
    }

    [Fact]
    public async Task GetByGarageAndPhone_returns_the_matching_invitation()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        await _sut.AddAsync(Factories.Invitation(garageId, roleId, phone: "+15559999"));

        var found = await _sut.GetByGarageAndPhoneAsync(garageId, "+15559999");
        Assert.NotNull(found);
        Assert.Equal("+15559999", found!.Phone);

        Assert.Null(await _sut.GetByGarageAndPhoneAsync(garageId, "+10000000"));
    }

    [Fact]
    public async Task GetGarageInvitation_projects_the_role_name()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync("Senior Mechanic");
        var invitation = Factories.Invitation(garageId, roleId, phone: "+15558888");
        await _sut.AddAsync(invitation);

        var item = await _sut.GetGarageInvitationAsync(garageId, invitation.Id);

        Assert.NotNull(item);
        Assert.Equal(invitation.Id, item!.Id);
        Assert.Equal("+15558888", item.Phone);
        Assert.Equal(roleId, item.RoleId);
        Assert.Equal("Senior Mechanic", item.RoleName);
    }

    [Fact]
    public async Task GetGarageInvitation_returns_null_for_another_garage()
    {
        var (garageA, roleA) = await SeedGarageAndRoleAsync();
        var (garageB, _) = await SeedGarageAndRoleAsync();
        var invitation = Factories.Invitation(garageA, roleA);
        await _sut.AddAsync(invitation);

        // The invitation exists, but not in garage B.
        Assert.Null(await _sut.GetGarageInvitationAsync(garageB, invitation.Id));
    }

    [Fact]
    public async Task ListByGarage_returns_all_invitations_newest_first()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        await _sut.AddAsync(Factories.Invitation(
            garageId, roleId, phone: "+15550001", createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await _sut.AddAsync(Factories.Invitation(
            garageId, roleId, phone: "+15550002", createdAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        var list = await _sut.ListByGarageAsync(garageId);

        Assert.Equal(2, list.Count);
        Assert.Equal("+15550002", list[0].Phone); // newer first
        Assert.Equal("+15550001", list[1].Phone);
    }

    [Fact]
    public async Task ListByGarage_returns_empty_for_a_garage_with_none()
    {
        var (garageId, _) = await SeedGarageAndRoleAsync();
        Assert.Empty(await _sut.ListByGarageAsync(garageId));
    }

    [Fact]
    public async Task ListByPhone_projects_garage_and_role_names_across_garages()
    {
        var (garageA, roleA) = await SeedGarageAndRoleAsync("Mechanic");
        var (garageB, roleB) = await SeedGarageAndRoleAsync("Manager");
        await _sut.AddAsync(Factories.Invitation(garageA, roleA, phone: "+15557777"));
        await _sut.AddAsync(Factories.Invitation(garageB, roleB, phone: "+15557777"));
        // A different phone should not appear.
        await _sut.AddAsync(Factories.Invitation(garageA, roleA, phone: "+15550000"));

        var list = await _sut.ListByPhoneAsync("+15557777");

        Assert.Equal(2, list.Count);
        Assert.Contains(list, i => i.GarageId == garageA && i.RoleName == "Mechanic");
        Assert.Contains(list, i => i.GarageId == garageB && i.RoleName == "Manager");
        Assert.All(list, i => Assert.Equal("Test Garage", i.GarageName));
    }

    [Fact]
    public async Task Delete_removes_the_invitation()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        var invitation = Factories.Invitation(garageId, roleId);
        await _sut.AddAsync(invitation);

        Assert.True(await _sut.DeleteAsync(invitation.Id));
        Assert.Null(await _sut.GetByIdAsync(invitation.Id));
    }

    [Fact]
    public async Task Delete_returns_false_when_missing()
    {
        Assert.False(await _sut.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_a_role_cascades_to_its_pending_invitations()
    {
        var (garageId, roleId) = await SeedGarageAndRoleAsync();
        var invitation = Factories.Invitation(garageId, roleId);
        await _sut.AddAsync(invitation);

        await using (var cmd = _fixture.DataSource.CreateCommand("DELETE FROM roles WHERE id = @id"))
        {
            cmd.Parameters.AddWithValue("id", roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Null(await _sut.GetByIdAsync(invitation.Id));
    }
}
