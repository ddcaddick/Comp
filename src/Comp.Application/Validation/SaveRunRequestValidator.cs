using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class SaveRunRequestValidator : AbstractValidator<SaveRunRequest>
{
    public SaveRunRequestValidator()
    {
        RuleFor(x => x.PenaltyCount).GreaterThanOrEqualTo(0);

        RuleFor(x => x.RawTimeMs)
            .NotNull()
            .WithMessage("A run that is not DNF must have a raw time.")
            .When(x => !x.IsDnf);

        RuleFor(x => x.RawTimeMs)
            .GreaterThan(0)
            .WithMessage("Raw time must be positive.")
            .When(x => x.RawTimeMs.HasValue);
    }
}
