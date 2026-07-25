using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="AssignRoleDto"/>. Checks input shape only (role id present); role
/// ownership, the escalation guard, and the last-staff-manager guard are enforced in
/// <c>MembershipService</c>.
/// </summary>
public class AssignRoleDtoValidator : AbstractValidator<AssignRoleDto>
{
    public AssignRoleDtoValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("A role id is required.");
    }
}
