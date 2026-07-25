using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="UpdateRoleDto"/>. Same input-shape checks as create; system-role
/// immutability and the escalation guard are enforced in <c>RoleService</c>.
/// </summary>
public class UpdateRoleDtoValidator : AbstractValidator<UpdateRoleDto>
{
    public UpdateRoleDtoValidator()
    {
        RuleFor(x => x.Name).ValidRoleName();
        RuleFor(x => x.Scopes).ValidRoleScopes();
    }
}
