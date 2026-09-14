using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Auth;

namespace Comp.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth");

        auth.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
                (await authService.LoginAsync(request, ct)) switch
                {
                    AuthResult.Success success => Results.Ok(success.Tokens),
                    AuthResult.Failure failure => Results.Problem(detail: failure.Reason, statusCode: StatusCodes.Status401Unauthorized),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting("auth")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/refresh", async (RefreshRequest request, IAuthService authService, CancellationToken ct) =>
                (await authService.RefreshAsync(request, ct)) switch
                {
                    AuthResult.Success success => Results.Ok(success.Tokens),
                    AuthResult.Failure failure => Results.Problem(detail: failure.Reason, statusCode: StatusCodes.Status401Unauthorized),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<RefreshRequest>>()
            .RequireRateLimiting("auth")
            .AllowAnonymous()
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
