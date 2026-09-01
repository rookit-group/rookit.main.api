using MainHub.Api.Validators;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure unit tests for CreateInvitationDtoValidator (input shape only: a plausible phone and a role id).
public class InvitationValidatorTests
{
    private readonly CreateInvitationDtoValidator _sut = new();

    [Fact]
    public void Passes_when_phone_and_role_id_are_present()
    {
        var dto = new CreateInvitationDto { Phone = "+1234567890", RoleId = Guid.NewGuid() };
        Assert.True(_sut.Validate(dto).IsValid);
    }

    [Theory]
    [InlineData("+380 (67) 123-45-67")]
    [InlineData("0671234567")]
    [InlineData("+1-800-555-0199")]
    public void Accepts_common_phone_formats(string phone)
    {
        var dto = new CreateInvitationDto { Phone = phone, RoleId = Guid.NewGuid() };
        Assert.True(_sut.Validate(dto).IsValid);
    }

    [Fact]
    public void Rejects_an_empty_phone()
    {
        var dto = new CreateInvitationDto { Phone = "", RoleId = Guid.NewGuid() };
        var result = _sut.Validate(dto);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateInvitationDto.Phone));
    }

    [Theory]
    [InlineData("not-a-phone")]
    [InlineData("123")]
    public void Rejects_a_malformed_phone(string phone)
    {
        var dto = new CreateInvitationDto { Phone = phone, RoleId = Guid.NewGuid() };
        var result = _sut.Validate(dto);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateInvitationDto.Phone));
    }

    [Fact]
    public void Rejects_an_empty_role_id()
    {
        var dto = new CreateInvitationDto { Phone = "+1234567890", RoleId = Guid.Empty };
        var result = _sut.Validate(dto);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateInvitationDto.RoleId));
    }
}
