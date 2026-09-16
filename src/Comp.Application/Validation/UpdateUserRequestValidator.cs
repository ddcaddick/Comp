using Comp.Contracts.Users;
using FluentValidation;

namespace Comp.Application.Validation;

public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty();
    }
}
