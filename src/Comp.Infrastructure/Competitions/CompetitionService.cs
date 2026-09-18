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

        return new CompetitionResult.Success(await ToResponseAsync(competition, cancellationToken));
    }

    public async Task<IReadOnlyList<CompetitionResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var competitions = await dbContext.Competitions
            .OrderByDescending(c => c.Year)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var competitionIds = competitions.Select(c => c.Id).ToList();

        var events = await dbContext.Events
            .Where(e => competitionIds.Contains(e.CompetitionId))
            .ToListAsync(cancellationToken);
        var eventIds = events.Select(e => e.Id).ToList();

        var participants = await dbContext.EventParticipants
            .Where(p => eventIds.Contains(p.EventId))
            .ToListAsync(cancellationToken);
        var participantIds = participants.Select(p => p.Id).ToList();

        // "Shot" means at least one run exists for that participant, DNF or not -- the
        // same Shot/NotRun distinction the mobile squad list's stats bar uses (see
        // Comp.Scoring's ParticipantScoringStatus.NotRun). Someone only ever added to a
        // roster and never called up doesn't count as having shot the competition.
        var shotParticipantIds = (await dbContext.Runs
                .Where(r => participantIds.Contains(r.EventParticipantId))
                .Select(r => r.EventParticipantId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var rosteredCountByCompetition = (await dbContext.LeagueMemberships
                .Where(m => competitionIds.Contains(m.CompetitionId))
                .GroupBy(m => m.CompetitionId)
                .Select(g => new { CompetitionId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.CompetitionId, x => x.Count);

        var eventsByCompetition = events.ToLookup(e => e.CompetitionId);
        var participantsByEvent = participants.ToLookup(p => p.EventId);

        return competitions
            .Select(c => BuildResponse(
                c, eventsByCompetition[c.Id], participantsByEvent, shotParticipantIds,
                rosteredCountByCompetition.GetValueOrDefault(c.Id)))
            .ToList();
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

        return new CompetitionResult.Success(await ToResponseAsync(competition, cancellationToken));
    }

    // Used by CreateAsync/CloseAsync, which each handle a single competition -- ListAsync
    // has its own batched version of this same query shape to avoid N+1 round trips.
    private async Task<CompetitionResponse> ToResponseAsync(Competition competition, CancellationToken cancellationToken)
    {
        var events = await dbContext.Events
            .Where(e => e.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken);
        var eventIds = events.Select(e => e.Id).ToList();

        var participants = await dbContext.EventParticipants
            .Where(p => eventIds.Contains(p.EventId))
            .ToListAsync(cancellationToken);
        var participantIds = participants.Select(p => p.Id).ToList();

        var shotParticipantIds = (await dbContext.Runs
                .Where(r => participantIds.Contains(r.EventParticipantId))
                .Select(r => r.EventParticipantId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var rosteredCount = await dbContext.LeagueMemberships
            .CountAsync(m => m.CompetitionId == competition.Id, cancellationToken);

        return BuildResponse(competition, events, participants.ToLookup(p => p.EventId), shotParticipantIds, rosteredCount);
    }

    private static CompetitionResponse BuildResponse(
        Competition competition,
        IEnumerable<Event> competitionEvents,
        ILookup<Guid, EventParticipant> participantsByEvent,
        HashSet<Guid> shotParticipantIds,
        int rosteredCount)
    {
        var eventsList = competitionEvents.ToList();

        var shooterIdsWhoShot = new HashSet<Guid>();
        var perEventShooterCounts = new List<int>();
        foreach (var @event in eventsList)
        {
            var shootersThisEvent = new HashSet<Guid>();
            foreach (var participant in participantsByEvent[@event.Id])
            {
                if (!shotParticipantIds.Contains(participant.Id))
                {
                    continue;
                }

                shootersThisEvent.Add(participant.ShooterId);
                shooterIdsWhoShot.Add(participant.ShooterId);
            }

            // Only an event that actually had someone shoot counts towards the average --
            // a future, empty week would otherwise drag it down for no reason.
            if (shootersThisEvent.Count > 0)
            {
                perEventShooterCounts.Add(shootersThisEvent.Count);
            }
        }

        var averagePerEvent = perEventShooterCounts.Count > 0
            ? Math.Round((decimal)perEventShooterCounts.Sum() / perEventShooterCounts.Count, 1)
            : 0m;
        var eventsRemaining = eventsList.Count(e => e.Status != EventStatus.Finalised);

        return new CompetitionResponse(
            competition.Id,
            competition.Name,
            competition.Year,
            competition.StartsOn,
            competition.EndsOn,
            competition.Status.ToString(),
            shooterIdsWhoShot.Count,
            averagePerEvent,
            eventsRemaining,
            rosteredCount);
    }
}
