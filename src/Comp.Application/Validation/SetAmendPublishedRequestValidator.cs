using Comp.Contracts.Users;
using FluentValidation;

namespace Comp.Application.Validation;

public class SetAmendPublishedRequestValidator : AbstractValidator<SetAmendPublishedRequest>
{
    public SetAmendPublishedRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
