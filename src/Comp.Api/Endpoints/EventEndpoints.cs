using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Infrastructure.Identity;

namespace Comp.Api.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // Any authenticated role may view; per the security model (architecture doc
        // section K), creating/editing events is Super Admin/Admin only, while managing
        // participants and squads also allows Official.
        var events = app.MapGroup("/events").RequireAuthorization();

        events.MapGet("", async (Guid? competitionId, string? status, IEventService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(competitionId, status, ct)))
            .Produces<IReadOnlyList<EventResponse>>();

        events.MapPost("", async (CreateEventRequest request, IEventService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)) switch
                {
                    EventCommandResult.Success success => Results.Created($"/events/{success.Event.Id}", success.Event),
                    EventCommandResult.NotFound => Results.NotFound(),
                    EventCommandResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<CreateEventRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin))
            .Produces<EventResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapPatch("/{id:guid}", async (Guid id, UpdateEventRequest request, IEventService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)) switch
                {
                    EventCommandResult.Success success => Results.Ok(success.Event),
                    EventCommandResult.NotFound => Results.NotFound(),
                    EventCommandResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<UpdateEventRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin))
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapPost("/{id:guid}/transition", async (Guid id, TransitionEventRequest request, IEventService service, CancellationToken ct) =>
                (await service.TransitionAsync(id, request, ct)) switch
                {
                    EventCommandResult.Success success => Results.Ok(success.Event),
                    EventCommandResult.NotFound => Results.NotFound(),
                    EventCommandResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<TransitionEventRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin))
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Unlocking a finalised event needs both the role (per the security model's
        // "Finalise and publish"/"Amend a published result" rows — Super Admin/Admin only)
        // and the per-user amend-published claim; only a super admin grants that claim.
        events.MapPost("/{id:guid}/amend", async (Guid id, AmendEventRequest request, IEventService service, CancellationToken ct) =>
                (await service.AmendAsync(id, request, ct)) switch
                {
                    EventCommandResult.Success success => Results.Ok(success.Event),
                    EventCommandResult.NotFound => Results.NotFound(),
                    EventCommandResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<AmendEventRequest>>()
            .RequireAuthorization(policy => policy
                .RequireRole(Roles.SuperAdmin, Roles.Admin)
                .RequireClaim(AppUserClaimsPrincipalFactory.AmendPublishedClaimType, "true"))
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapGet("/{id:guid}/results", async (Guid id, Guid? leagueId, IResultsService service, CancellationToken ct) =>
            {
                var results = await service.GetEventResultsAsync(id, leagueId, ct);
                return results is null ? Results.NotFound() : Results.Ok(results);
            })
            .Produces<EventResultsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        events.MapGet("/{id:guid}/participants", async (Guid id, IEventParticipantService service, CancellationToken ct) =>
            {
                var participants = await service.ListAsync(id, ct);
                return participants is null ? Results.NotFound() : Results.Ok(participants);
            })
            .Produces<IReadOnlyList<EventParticipantResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        events.MapPost("/{id:guid}/participants", async (Guid id, AddParticipantRequest request, IEventParticipantService service, CancellationToken ct) =>
                (await service.AddAsync(id, request, ct)) switch
                {
                    EventParticipantResult.Success success =>
                        Results.Created($"/events/{id}/participants/{success.Participant.Id}", success.Participant),
                    EventParticipantResult.NotFound => Results.NotFound(),
                    EventParticipantResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<AddParticipantRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<EventParticipantResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapPatch("/{id:guid}/participants/{participantId:guid}",
                async (Guid id, Guid participantId, UpdateParticipantRequest request, IEventParticipantService service, CancellationToken ct) =>
                    (await service.UpdateAsync(id, participantId, request, ct)) switch
                    {
                        EventParticipantResult.Success success => Results.Ok(success.Participant),
                        EventParticipantResult.NotFound => Results.NotFound(),
                        EventParticipantResult.Conflict conflict =>
                            Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                    })
            .AddEndpointFilter<ValidationFilter<UpdateParticipantRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<EventParticipantResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapDelete("/{id:guid}/participants/{participantId:guid}",
                async (Guid id, Guid participantId, IEventParticipantService service, CancellationToken ct) =>
                    (await service.RemoveAsync(id, participantId, ct)) switch
                    {
                        EventParticipantResult.Removed => Results.NoContent(),
                        EventParticipantResult.NotFound => Results.NotFound(),
                        EventParticipantResult.Conflict conflict =>
                            Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                    })
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapGet("/{id:guid}/squads", async (Guid id, ISquadService service, CancellationToken ct) =>
            {
                var squads = await service.ListAsync(id, ct);
                return squads is null ? Results.NotFound() : Results.Ok(squads);
            })
            .Produces<IReadOnlyList<SquadResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        events.MapPost("/{id:guid}/squads", async (Guid id, CreateSquadRequest request, ISquadService service, CancellationToken ct) =>
                (await service.CreateAsync(id, request, ct)) switch
                {
                    SquadResult.Success success => Results.Created($"/events/{id}/squads/{success.Squad.Id}", success.Squad),
                    SquadResult.NotFound => Results.NotFound(),
                    SquadResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<CreateSquadRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<SquadResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        events.MapPost("/{id:guid}/squads/{squadId:guid}/complete", async (Guid id, Guid squadId, ISquadService service, CancellationToken ct) =>
                (await service.CompleteAsync(id, squadId, ct)) switch
                {
                    SquadResult.Success success => Results.Ok(success.Squad),
                    SquadResult.NotFound => Results.NotFound(),
                    SquadResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Official))
            .Produces<SquadResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
