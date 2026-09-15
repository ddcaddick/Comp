using Comp.Contracts.Events;
using Comp.Domain.Enums;
using FluentValidation;

namespace Comp.Application.Validation;

public class TransitionEventRequestValidator : AbstractValidator<TransitionEventRequest>
{
    public TransitionEventRequestValidator()
    {
        RuleFor(x => x.To)
            .NotEmpty()
            .Must(to => Enum.TryParse<EventStatus>(to, ignoreCase: true, out _))
            .WithMessage("'To' must be one of: " + string.Join(", ", Enum.GetNames<EventStatus>()));
    }
}
