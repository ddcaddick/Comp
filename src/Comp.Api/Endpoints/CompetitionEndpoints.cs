using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Competitions;
using Comp.Contracts.Leagues;
using Comp.Infrastructure.Identity;

namespace Comp.Api.Endpoints;

public static class CompetitionEndpoints
{
    public static void MapCompetitionEndpoints(this IEndpointRouteBuilder app)
    {
        // Managing competitions and leagues is Super Admin only, per the architecture
        // doc's security model (section K) — but any authenticated role may view them.
        var competitions = app.MapGroup("/competitions").RequireAuthorization();

        competitions.MapGet("", async (ICompetitionService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<CompetitionResponse>>();

        competitions.MapPost("", async (CreateCompetitionRequest request, ICompetitionService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)) switch
                {
                    CompetitionResult.Success success =>
                        Results.Created($"/competitions/{success.Competition.Id}", success.Competition),
                    CompetitionResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<CreateCompetitionRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin))
            .Produces<CompetitionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        competitions.MapPost("/{id:guid}/close", async (Guid id, ICompetitionService service, CancellationToken ct) =>
                (await service.CloseAsync(id, ct)) switch
                {
                    CompetitionResult.Success success => Results.Ok(success.Competition),
                    CompetitionResult.NotFound => Results.NotFound(),
                    CompetitionResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin))
            .Produces<CompetitionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        competitions.MapGet("/{id:guid}/leagues", async (Guid id, ILeagueService service, CancellationToken ct) =>
            {
                var leagues = await service.ListForCompetitionAsync(id, ct);
                return leagues is null ? Results.NotFound() : Results.Ok(leagues);
            })
            .Produces<IReadOnlyList<LeagueResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        competitions.MapPost("/{id:guid}/leagues", async (Guid id, CreateLeagueRequest request, ILeagueService service, CancellationToken ct) =>
                (await service.CreateAsync(id, request, ct)) switch
                {
                    LeagueResult.Success success =>
                        Results.Created($"/competitions/{id}/leagues/{success.League.Id}", success.League),
                    LeagueResult.NotFound => Results.NotFound(),
                    LeagueResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<CreateLeagueRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin))
            .Produces<LeagueResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
