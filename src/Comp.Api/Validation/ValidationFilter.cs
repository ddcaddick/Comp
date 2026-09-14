using FluentValidation;

namespace Comp.Api.Validation;

/// <summary>
/// Runs the registered <see cref="IValidator{T}"/> (if any) against the first argument of
/// type <typeparamref name="T"/> before the endpoint runs, returning an RFC 7807 validation
/// problem on failure. Wired per-endpoint with <c>AddEndpointFilter&lt;ValidationFilter&lt;T&gt;&gt;()</c>.
/// </summary>
public class ValidationFilter<T> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context);
        }

        var validator = context.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (validator is null)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(argument);
        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.ToDictionary());
        }

        return await next(context);
    }
}
