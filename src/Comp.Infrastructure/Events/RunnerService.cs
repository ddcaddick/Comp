using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

public class RunnerService(CompDbContext dbContext) : IRunnerService
{
    public async Task<SquadRunnerResponse?> GetAsync(Guid eventId, Guid squadId, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event is null)
        {
            return null;
        }

        var squad = await dbContext.Squads
            .SingleOrDefaultAsync(s => s.Id == squadId && s.EventId == eventId, cancellationToken);
        if (squad is null)
        {
            return null;
        }

        var participants = await dbContext.EventParticipants
            .Where(p => p.SquadId == squadId)
            .Join(dbContext.Shooters, p => p.ShooterId, s => s.Id, (p, s) => new { p, s })
            .OrderBy(x => x.p.PositionInSquad)
            .ToListAsync(cancellationToken);

        var participantIds = participants.Select(x => x.p.Id).ToList();
        var runsByParticipant = (await dbContext.Runs
                .Where(r => participantIds.Contains(r.EventParticipantId))
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.EventParticipantId);

        var participantResponses = participants
            .Select(x => BuildParticipantResponse(x.p, x.s, runsByParticipant[x.p.Id], @event.RunsPerShooter))
            .ToList();

        return new SquadRunnerResponse(squad.Id, squad.SquadNumber, squad.Name, squad.Status.ToString(), participantResponses);
    }

    internal static RunnerParticipantResponse BuildParticipantResponse(
        EventParticipant participant, Shooter shooter, IEnumerable<Run> runs, int runsPerShooter)
    {
        var runsByNumber = runs.ToDictionary(r => r.RunNumber);
        var runStates = new List<RunnerRunState>(runsPerShooter);
        for (var n = 1; n <= runsPerShooter; n++)
        {
            runStates.Add(runsByNumber.TryGetValue(n, out var run)
                ? new RunnerRunState(n, IsRecorded: true, run.RawTimeMs, run.PenaltyCount, run.IsDnf)
                : new RunnerRunState(n, IsRecorded: false, null, 0, false));
        }

        return new RunnerParticipantResponse(
            participant.Id, shooter.Id, shooter.FirstName, shooter.LastName, participant.PositionInSquad, runStates);
    }
}
