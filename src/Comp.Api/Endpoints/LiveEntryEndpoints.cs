using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Comp.Api.Endpoints;

public static class LiveEntryEndpoints
{
    public static void MapLiveEntryEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/events").RequireAuthorization();

        events.MapGet("/{id:guid}/squads/{squadId:guid}/runner",
                async (Guid id, Guid squadId, IRunnerService service, CancellationToken ct) =>
                {
                    var runner = await service.GetAsync(id, squadId, ct);
                    return runner is null ? Results.NotFound() : Results.Ok(runner);
                })
            .Produces<SquadRunnerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Recording a run allows Official (and Admin/Super Admin) per the security
        // model's "Record runs" row.
        events.MapPut("/{id:guid}/participants/{participantId:guid}/runs/{runNumber:int}",
                async (Guid id, Guid participantId, int runNumber, SaveRunRequest request,
                        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                        IRunService service, CancellationToken ct) =>
                    (await service.SaveRunAsync(id, participantId, runNumber, request, idempotencyKey, ct)) switch
                    {
                        RunResult.Success success => Results.Ok(success.Response),
                        RunResult.NotFound => Results.NotFound(),
                        RunResult.Conflict conflict =>
                            Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                    })
            .AddEndpointFilter<ValidationFilter<SaveRunRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<SaveRunResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapGet("/{id:guid}/entry-session", async (Guid id, IEntrySessionService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)) switch
                {
                    EntrySessionResult.Success success => Results.Ok(success.Session),
                    EntrySessionResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .Produces<EntrySessionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        events.MapPost("/{id:guid}/entry-session/heartbeat", async (Guid id, IEntrySessionService service, CancellationToken ct) =>
                (await service.HeartbeatAsync(id, ct)) switch
                {
                    EntrySessionResult.Success success => Results.Ok(success.Session),
                    EntrySessionResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<EntrySessionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
