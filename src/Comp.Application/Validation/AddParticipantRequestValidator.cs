using Comp.Contracts.Events;
using FluentValidation;

namespace Comp.Application.Validation;

public class AddParticipantRequestValidator : AbstractValidator<AddParticipantRequest>
{
    public AddParticipantRequestValidator()
    {
        RuleFor(x => x.ShooterId).NotEmpty();
    }
}
