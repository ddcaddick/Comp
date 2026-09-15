using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class CreateEventRequestValidator : AbstractValidator<CreateEventRequest>
{
    public CreateEventRequestValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.EventNumber).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PenaltySeconds).GreaterThanOrEqualTo(0).When(x => x.PenaltySeconds.HasValue);
        RuleFor(x => x.RunsPerShooter).GreaterThan(0).When(x => x.RunsPerShooter.HasValue);
    }
}
