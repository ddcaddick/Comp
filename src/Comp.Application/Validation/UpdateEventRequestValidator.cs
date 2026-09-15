using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class UpdateEventRequestValidator : AbstractValidator<UpdateEventRequest>
{
    public UpdateEventRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PenaltySeconds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RunsPerShooter).GreaterThan(0);
    }
}
