using Comp.Contracts.Leagues;
using FluentValidation;

namespace Comp.Application.Validation;

public class SetLeagueMembersRequestValidator : AbstractValidator<SetLeagueMembersRequest>
{
    public SetLeagueMembersRequestValidator()
    {
        RuleFor(x => x.ShooterIds).NotNull();
        RuleForEach(x => x.ShooterIds).NotEmpty();
    }
}
