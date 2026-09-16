using Comp.Contracts.Users;
using FluentValidation;

namespace Comp.Application.Validation;

public class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty();
    }
}
