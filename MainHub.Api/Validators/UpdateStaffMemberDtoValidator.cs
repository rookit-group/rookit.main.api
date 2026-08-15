using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="UpdateStaffMemberDto"/>. Checks input shape only (role id present, and
/// name/email well-formed when supplied); role ownership, the escalation guard, and the
/// last-staff-manager guard are enforced in <c>MembershipService</c>.
/// </summary>
public class UpdateStaffMemberDtoValidator : AbstractValidator<UpdateStaffMemberDto>
{
    public UpdateStaffMemberDtoValidator()
    {
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("A role id is required.");

        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .MaximumLength(255).WithMessage("Email must not exceed 255 characters.")
            .When(x => x.Email is not null);
    }
}
