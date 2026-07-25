using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="CreateRoleDto"/>. Checks input shape only (name present, scopes are a
/// non-empty, duplicate-free subset of the catalog); the escalation guard and system-role rules are
/// enforced in <c>RoleService</c>.
/// </summary>
public class CreateRoleDtoValidator : AbstractValidator<CreateRoleDto>
{
    public CreateRoleDtoValidator()
    {
        RuleFor(x => x.Name).ValidRoleName();
        RuleFor(x => x.Scopes).ValidRoleScopes();
    }
}
