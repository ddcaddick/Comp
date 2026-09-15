using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class CreateSquadRequestValidator : AbstractValidator<CreateSquadRequest>
{
    public CreateSquadRequestValidator()
    {
        RuleFor(x => x.SquadNumber).GreaterThan(0).When(x => x.SquadNumber.HasValue);
        RuleFor(x => x.Name).MaximumLength(100);
    }
}
