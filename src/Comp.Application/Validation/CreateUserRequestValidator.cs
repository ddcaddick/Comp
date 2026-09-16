using Comp.Contracts.Users;
using FluentValidation;

namespace Comp.Application.Validation;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        // Whether this is one of the four known roles is checked in the service, which
        // already needs Comp.Infrastructure.Identity.Roles for the assignment itself --
        // Comp.Application can't reference it without a circular project dependency.
        RuleFor(x => x.Role).NotEmpty();
        // Identity's own password policy (ASP.NET Core Identity's defaults) is the real
        // validation here, enforced when the account is actually created; duplicating it
        // in FluentValidation would just be a second, driftable copy of the same rules.
        RuleFor(x => x.Password).NotEmpty();
    }
}
