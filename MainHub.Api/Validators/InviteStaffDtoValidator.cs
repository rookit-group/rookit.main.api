using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="InviteStaffDto"/>. Checks input shape only (both ids present); whether
/// the user exists, the role belongs to the garage, and the caller may grant it are enforced in
/// <c>MembershipService</c>.
/// </summary>
public class InviteStaffDtoValidator : AbstractValidator<InviteStaffDto>
{
    public InviteStaffDtoValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("A user id is required.");
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("A role id is required.");
    }
}
