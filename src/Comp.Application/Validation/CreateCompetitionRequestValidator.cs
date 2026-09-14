using Comp.Contracts.Competitions;
using FluentValidation;

namespace Comp.Application.Validation;

public class CreateCompetitionRequestValidator : AbstractValidator<CreateCompetitionRequest>
{
    public CreateCompetitionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.EndsOn).GreaterThanOrEqualTo(x => x.StartsOn)
            .WithMessage("EndsOn must not be before StartsOn.");
    }
}
