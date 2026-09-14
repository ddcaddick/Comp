using Comp.Application.Abstractions;
using Comp.Contracts.Competitions;
using Comp.Domain;
using Comp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Competitions;

public class CompetitionService(CompDbContext dbContext) : ICompetitionService
{
    public async Task<CompetitionResult> CreateAsync(CreateCompetitionRequest request, CancellationToken cancellationToken)
    {
        // Service-level check ahead of the unique(year, name) index, so a duplicate
        // reads as a clear conflict rather than a raw constraint-violation 500.
        var duplicate = await dbContext.Competitions
            .AnyAsync(c => c.Year == request.Year && c.Name == request.Name, cancellationToken);
        if (duplicate)
        {
            return new CompetitionResult.Conflict(
                $"A competition named '{request.Name}' already exists for {request.Year}.");
        }

        var competition = new Competition
        {
            Name = request.Name,
            Year = request.Year,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn
        };
        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompetitionResult.Success(ToResponse(competition));
    }

    public async Task<IReadOnlyList<CompetitionResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var competitions = await dbContext.Competitions
            .OrderByDescending(c => c.Year)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return competitions.Select(ToResponse).ToList();
    }

    public async Task<CompetitionResult> CloseAsync(Guid id, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions.FindAsync([id], cancellationToken);
        if (competition is null)
        {
            return new CompetitionResult.NotFound();
        }

        if (competition.Status == CompetitionStatus.Closed)
        {
            return new CompetitionResult.Conflict("Competition is already closed.");
        }

        competition.Status = CompetitionStatus.Closed;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CompetitionResult.Success(ToResponse(competition));
    }

    private static CompetitionResponse ToResponse(Competition competition) => new(
        competition.Id,
        competition.Name,
        competition.Year,
        competition.StartsOn,
        competition.EndsOn,
        competition.Status.ToString());
}
