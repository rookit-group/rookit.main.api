using MainHub.Api.Validators;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure unit tests for the self-serve garage-create DTO validator (input shape only).
public class GarageValidatorTests
{
    private readonly CreateGarageDtoValidator _sut = new();

    [Fact]
    public void Passes_for_a_valid_name()
    {
        Assert.True(_sut.Validate(new CreateGarageDto { Name = "Downtown Motors" }).IsValid);
    }

    [Fact]
    public void Rejects_an_empty_name()
    {
        var result = _sut.Validate(new CreateGarageDto { Name = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateGarageDto.Name));
    }

    [Fact]
    public void Rejects_a_name_longer_than_100_characters()
    {
        var result = _sut.Validate(new CreateGarageDto { Name = new string('x', 101) });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateGarageDto.Name));
    }
}
