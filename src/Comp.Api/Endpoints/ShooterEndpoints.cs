using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Shooters;
using Comp.Infrastructure.Identity;

namespace Comp.Api.Endpoints;

public static class ShooterEndpoints
{
    public static void MapShooterEndpoints(this IEndpointRouteBuilder app)
    {
        // Any authenticated role may view the register; only specific roles may write to
        // it (see the per-endpoint RequireAuthorization calls below), per the security
        // model in the architecture doc's section K.
        var shooters = app.MapGroup("/shooters").RequireAuthorization();

        // recentFirst needs a C# default (and so has to come last) — minimal API query
        // binding treats a non-nullable bool parameter with none as required.
        shooters.MapGet("", async (string? q, bool? active, IShooterService service, CancellationToken ct, bool recentFirst = false) =>
                Results.Ok(await service.SearchAsync(q, active, recentFirst, ct)))
            .Produces<IReadOnlyList<ShooterResponse>>();

        shooters.MapGet("/{id:guid}/history", async (Guid id, IShooterService service, CancellationToken ct) =>
            {
                var history = await service.GetHistoryAsync(id, ct);
                return history is null ? Results.NotFound() : Results.Ok(history);
            })
            .Produces<IReadOnlyList<ShooterHistoryEntryResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        shooters.MapPost("", async (CreateShooterRequest request, IShooterService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/shooters/{created.Id}", created);
            })
            .AddEndpointFilter<ValidationFilter<CreateShooterRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<ShooterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        shooters.MapPatch("/{id:guid}", async (Guid id, UpdateShooterRequest request, IShooterService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)) switch
                {
                    ShooterResult.Success success => Results.Ok(success.Shooter),
                    ShooterResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<UpdateShooterRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin))
            .Produces<ShooterResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        shooters.MapPost("/{id:guid}/deactivate", async (Guid id, IShooterService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, ct)) switch
                {
                    ShooterResult.Success success => Results.Ok(success.Shooter),
                    ShooterResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin))
            .Produces<ShooterResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        shooters.MapPost("/{id:guid}/reactivate", async (Guid id, IShooterService service, CancellationToken ct) =>
                (await service.ReactivateAsync(id, ct)) switch
                {
                    ShooterResult.Success success => Results.Ok(success.Shooter),
                    ShooterResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin))
            .Produces<ShooterResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
