using Comp.Contracts.Leagues;
using FluentValidation;

namespace Comp.Application.Validation;

public class CreateLeagueRequestValidator : AbstractValidator<CreateLeagueRequest>
{
    public CreateLeagueRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Tier).GreaterThan(0);
        RuleFor(x => x.PointsForFirst).GreaterThan(0).When(x => x.PointsForFirst.HasValue);
        RuleFor(x => x.PointsDecrement).GreaterThanOrEqualTo(0).When(x => x.PointsDecrement.HasValue);
        RuleFor(x => x.DropWorstCount).GreaterThanOrEqualTo(0).When(x => x.DropWorstCount.HasValue);
    }
}
