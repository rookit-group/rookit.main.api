using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="CreateGarageDto"/>. Checks input shape only (name present and within a
/// sane length); ownership seeding and atomicity are handled by <c>GarageService</c>.
/// </summary>
public class CreateGarageDtoValidator : AbstractValidator<CreateGarageDto>
{
    public CreateGarageDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Garage name is required.")
            .MaximumLength(100).WithMessage("Garage name must not exceed 100 characters.");
    }
}
