using Comp.Contracts.Auth;
using FluentValidation;

namespace Comp.Application.Validation;

public class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
