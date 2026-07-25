using MainHub.Api.Authorization;
using MainHub.Api.Validators;
using Shared.Contracts.DTOs;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Pure unit tests for the role write-DTO validators. Both validators share RoleValidationRules, so
// the create validator is exercised in depth and the update validator gets a parity smoke test.
public class RoleValidatorTests
{
    private readonly CreateRoleDtoValidator _create = new();
    private readonly UpdateRoleDtoValidator _update = new();

    private static CreateRoleDto Create(string name, params string[] scopes) =>
        new() { Name = name, Description = null, Scopes = scopes };

    private static UpdateRoleDto Update(string name, params string[] scopes) =>
        new() { Name = name, Description = null, Scopes = scopes };

    [Fact]
    public void Passes_for_a_valid_role()
    {
        var result = _create.Validate(Create("Mechanic", Scope.GarageRead, Scope.StaffRead));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Allows_the_wildcard_scope()
    {
        // The escalation guard (RoleService), not the validator, decides who may grant the wildcard.
        var result = _create.Validate(Create("Owner", Scope.Wildcard));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_an_empty_name()
    {
        var result = _create.Validate(Create("", Scope.GarageRead));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateRoleDto.Name));
    }

    [Fact]
    public void Rejects_a_name_longer_than_100_characters()
    {
        var result = _create.Validate(Create(new string('x', 101), Scope.GarageRead));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateRoleDto.Name));
    }

    [Fact]
    public void Rejects_an_empty_scope_list()
    {
        var result = _create.Validate(Create("Mechanic"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateRoleDto.Scopes));
    }

    [Fact]
    public void Rejects_duplicate_scopes()
    {
        var result = _create.Validate(Create("Mechanic", Scope.GarageRead, Scope.GarageRead));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_an_unknown_scope()
    {
        var result = _create.Validate(Create("Mechanic", "garage:teleport"));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("garage:teleport"));
    }

    [Fact]
    public void Update_validator_shares_the_same_rules()
    {
        Assert.True(_update.Validate(Update("Mechanic", Scope.GarageRead)).IsValid);
        Assert.False(_update.Validate(Update("", "garage:teleport")).IsValid);
    }
}
