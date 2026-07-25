using FluentValidation;
using MainHub.Api.Authorization;

namespace MainHub.Api.Validators;

/// <summary>
/// Shared FluentValidation rules for role write DTOs (create/update), so the "name required" and
/// "scopes must be a non-empty, duplicate-free subset of the catalog" checks live in one place.
/// </summary>
internal static class RoleValidationRules
{
    public static IRuleBuilderOptions<T, string> ValidRoleName<T>(
        this IRuleBuilder<T, string> rule) =>
        rule
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(100).WithMessage("Role name must not exceed 100 characters.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> ValidRoleScopes<T>(
        this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule
            .NotNull().WithMessage("At least one scope is required.")
            .Must(scopes => scopes is { Count: > 0 })
                .WithMessage("At least one scope is required.")
            .Must(scopes => scopes is null || scopes.Count == scopes.Distinct().Count())
                .WithMessage("Scopes must not contain duplicates.")
            .Must(scopes => scopes is null || Scope.AreAllKnown(scopes))
                .WithMessage(BuildUnknownScopesMessage);

    private static string BuildUnknownScopesMessage<T>(T _, IReadOnlyList<string> scopes) =>
        $"Unknown scope(s): {string.Join(", ", Scope.UnknownScopes(scopes))}. " +
        $"Allowed scopes: {string.Join(", ", Scope.All)} (or '{Scope.Wildcard}').";
}
