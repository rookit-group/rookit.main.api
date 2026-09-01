using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="CreateInvitationDto"/>. Checks input shape only (a phone number that looks
/// plausible and a role id present); whether the role belongs to the garage, the caller may grant it,
/// and no duplicate invitation exists are enforced in <c>InvitationService</c>.
/// </summary>
public class CreateInvitationDtoValidator : AbstractValidator<CreateInvitationDto>
{
    public CreateInvitationDtoValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("A phone number is required.")
            .MaximumLength(32).WithMessage("The phone number is too long.")
            .Matches(@"^\+?[0-9\s\-()]{6,}$")
            .WithMessage("The phone number is not in a valid format.");

        RuleFor(x => x.RoleId).NotEmpty().WithMessage("A role id is required.");
    }
}
