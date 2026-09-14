using Comp.Contracts.Shooters;
using FluentValidation;

namespace Comp.Application.Validation;

public class CreateShooterRequestValidator : AbstractValidator<CreateShooterRequest>
{
    public CreateShooterRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Nickname).MaximumLength(100);
        RuleFor(x => x.MembershipNo).MaximumLength(50);
    }
}
