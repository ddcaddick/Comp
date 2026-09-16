using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Users;
using Comp.Infrastructure.Identity;

namespace Comp.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // "Manage users" is Super Admin only per the security model's table -- every
        // endpoint here is gated on that one role, unlike most other admin-CRUD in this
        // app, which is usually Super Admin + Admin.
        var users = app.MapGroup("/users").RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin));

        users.MapGet("", async (IUserService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<UserResponse>>();

        users.MapPost("", async (CreateUserRequest request, IUserService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)) switch
                {
                    UserResult.Success success => Results.Created($"/users/{success.User.Id}", success.User),
                    UserResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<CreateUserRequest>>()
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPatch("/{id:guid}", async (Guid id, UpdateUserRequest request, IUserService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)) switch
                {
                    UserResult.Success success => Results.Ok(success.User),
                    UserResult.NotFound => Results.NotFound(),
                    UserResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<UpdateUserRequest>>()
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/deactivate", async (Guid id, IUserService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, ct)) switch
                {
                    UserResult.Success success => Results.Ok(success.User),
                    UserResult.NotFound => Results.NotFound(),
                    UserResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/reactivate", async (Guid id, IUserService service, CancellationToken ct) =>
                (await service.ReactivateAsync(id, ct)) switch
                {
                    UserResult.Success success => Results.Ok(success.User),
                    UserResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/reset-password", async (Guid id, SetPasswordRequest request, IUserService service, CancellationToken ct) =>
                (await service.ResetPasswordAsync(id, request, ct)) switch
                {
                    UserResult.Success success => Results.Ok(success.User),
                    UserResult.NotFound => Results.NotFound(),
                    UserResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<SetPasswordRequest>>()
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/unlock", async (Guid id, IUserService service, CancellationToken ct) =>
                (await service.UnlockAsync(id, ct)) switch
                {
                    UserResult.Success success => Results.Ok(success.User),
                    UserResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/amend-published", async (Guid id, SetAmendPublishedRequest request, IUserService service, CancellationToken ct) =>
                (await service.SetAmendPublishedAsync(id, request, ct)) switch
                {
                    UserResult.Success success => Results.Ok(success.User),
                    UserResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<SetAmendPublishedRequest>>()
            .Produces<UserResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
