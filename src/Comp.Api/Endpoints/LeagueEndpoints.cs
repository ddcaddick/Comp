using Comp.Api.Validation;
using Comp.Application.Abstractions;
using Comp.Contracts.Leagues;
using Comp.Infrastructure.Identity;

namespace Comp.Api.Endpoints;

public static class LeagueEndpoints
{
    public static void MapLeagueEndpoints(this IEndpointRouteBuilder app)
    {
        var leagues = app.MapGroup("/leagues").RequireAuthorization();

        leagues.MapGet("/{id:guid}/members", async (Guid id, ILeagueService service, CancellationToken ct) =>
                (await service.GetMembersAsync(id, ct)) switch
                {
                    LeagueMembersResult.Success success => Results.Ok(success.Members),
                    LeagueMembersResult.NotFound => Results.NotFound(),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .Produces<IReadOnlyList<LeagueMemberResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        leagues.MapPut("/{id:guid}/members", async (Guid id, SetLeagueMembersRequest request, ILeagueService service, CancellationToken ct) =>
                (await service.SetMembersAsync(id, request, ct)) switch
                {
                    LeagueMembersResult.Success success => Results.Ok(success.Members),
                    LeagueMembersResult.NotFound => Results.NotFound(),
                    LeagueMembersResult.Conflict conflict =>
                        Results.Problem(detail: conflict.Reason, statusCode: StatusCodes.Status409Conflict),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                })
            .AddEndpointFilter<ValidationFilter<SetLeagueMembersRequest>>()
            .RequireAuthorization(policy => policy.RequireRole(Roles.SuperAdmin))
            .Produces<IReadOnlyList<LeagueMemberResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
