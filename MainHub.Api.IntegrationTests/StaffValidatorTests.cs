using MainHub.Api.Validators;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure unit tests for the staff write-DTO validators (input shape only: required ids present).
public class StaffValidatorTests
{
    private readonly UpdateStaffMemberDtoValidator _update = new();

    [Fact]
    public void Update_passes_when_role_id_is_present()
    {
        Assert.True(_update.Validate(new UpdateStaffMemberDto { RoleId = Guid.NewGuid() }).IsValid);
    }

    [Fact]
    public void Update_rejects_an_empty_role_id()
    {
        var result = _update.Validate(new UpdateStaffMemberDto { RoleId = Guid.Empty });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStaffMemberDto.RoleId));
    }

    [Fact]
    public void Update_rejects_a_malformed_email()
    {
        var result = _update.Validate(new UpdateStaffMemberDto { RoleId = Guid.NewGuid(), Email = "not-an-email" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStaffMemberDto.Email));
    }
}
