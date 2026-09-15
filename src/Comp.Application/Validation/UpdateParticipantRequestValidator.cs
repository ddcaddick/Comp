using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class UpdateParticipantRequestValidator : AbstractValidator<UpdateParticipantRequest>
{
    public UpdateParticipantRequestValidator()
    {
        RuleFor(x => x.PositionInSquad).GreaterThan(0).When(x => x.PositionInSquad.HasValue);
    }
}
