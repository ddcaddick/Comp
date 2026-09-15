using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class AmendEventRequestValidator : AbstractValidator<AmendEventRequest>
{
    public AmendEventRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
