using FluentValidation;
using Shared.Contracts.DTOs;

namespace MainHub.Api.Validators;

/// <summary>
/// Validator for <see cref="UpdateGarageDto"/>. Checks input shape only (name present and within a
/// sane length); the <c>garage:manage</c> scope is enforced at the endpoint.
/// </summary>
public class UpdateGarageDtoValidator : AbstractValidator<UpdateGarageDto>
{
    public UpdateGarageDtoValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Garage name is required.")
            .MaximumLength(100).WithMessage("Garage name must not exceed 100 characters.");
    }
}
