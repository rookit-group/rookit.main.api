using MainHub.Api.Validators;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure unit tests for the staff write-DTO validators (input shape only: required ids present).
public class StaffValidatorTests
{
    private readonly InviteStaffDtoValidator _invite = new();
    private readonly AssignRoleDtoValidator _assign = new();

    [Fact]
    public void Invite_passes_when_both_ids_are_present()
    {
        var dto = new InviteStaffDto { UserId = Guid.NewGuid(), RoleId = Guid.NewGuid() };
        Assert.True(_invite.Validate(dto).IsValid);
    }

    [Fact]
    public void Invite_rejects_an_empty_user_id()
    {
        var dto = new InviteStaffDto { UserId = Guid.Empty, RoleId = Guid.NewGuid() };
        var result = _invite.Validate(dto);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InviteStaffDto.UserId));
    }

    [Fact]
    public void Invite_rejects_an_empty_role_id()
    {
        var dto = new InviteStaffDto { UserId = Guid.NewGuid(), RoleId = Guid.Empty };
        var result = _invite.Validate(dto);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InviteStaffDto.RoleId));
    }

    [Fact]
    public void Assign_passes_when_role_id_is_present()
    {
        Assert.True(_assign.Validate(new AssignRoleDto { RoleId = Guid.NewGuid() }).IsValid);
    }

    [Fact]
    public void Assign_rejects_an_empty_role_id()
    {
        var result = _assign.Validate(new AssignRoleDto { RoleId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AssignRoleDto.RoleId));
    }
}
