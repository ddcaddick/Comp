using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Domain;
using Comp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

public class RunService(CompDbContext dbContext, ICurrentUserAccessor currentUser) : IRunService
{
    public async Task<RunResult> SaveRunAsync(
        Guid eventId,
        Guid participantId,
        int runNumber,
        SaveRunRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event is null)
        {
            return new RunResult.NotFound();
        }

        // Belt and suspenders: the database trigger backs this up too (docs/m2-wiring.md),
        // but a clear 409 here beats a raw constraint-violation 500.
        if (@event.Status == EventStatus.Finalised)
        {
            return new RunResult.Conflict("Cannot record runs on a finalised event.");
        }

        if (runNumber < 1 || runNumber > @event.RunsPerShooter)
        {
            return new RunResult.Conflict($"This event only records {@event.RunsPerShooter} run(s) per shooter.");
        }

        var participant = await dbContext.EventParticipants
            .SingleOrDefaultAsync(p => p.Id == participantId && p.EventId == eventId, cancellationToken);
        if (participant is null)
        {
            return new RunResult.NotFound();
        }

        // Idempotency: a retried save (e.g. after a dropped response) replays the original
        // result rather than being applied twice.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existingByKey = await dbContext.Runs
                .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingByKey is not null)
            {
                var replayNext = await FindNextOutstandingAsync(
                    @event, participant.SquadId, existingByKey.RunNumber, existingByKey.EventParticipantId, cancellationToken);
                return new RunResult.Success(new SaveRunResponse(ToRunResponse(existingByKey), replayNext));
            }
        }

        var run = await dbContext.Runs
            .SingleOrDefaultAsync(r => r.EventParticipantId == participantId && r.RunNumber == runNumber, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var rawTimeMs = request.IsDnf ? null : request.RawTimeMs;

        if (run is null)
        {
            run = new Run
            {
                EventParticipantId = participantId,
                RunNumber = runNumber,
                RawTimeMs = rawTimeMs,
                PenaltyCount = request.PenaltyCount,
                IsDnf = request.IsDnf,
                RecordedByUserId = RequireActorId(),
                RecordedAt = now,
                UpdatedAt = now,
                IdempotencyKey = idempotencyKey
            };
            dbContext.Runs.Add(run);
        }
        else
        {
            // Overwriting an existing time is allowed — the mobile UI confirms and shows
            // the old value before calling this (architecture doc section J); the API
            // itself doesn't need to gate it further.
            run.RawTimeMs = rawTimeMs;
            run.PenaltyCount = request.PenaltyCount;
            run.IsDnf = request.IsDnf;
            run.UpdatedAt = now;
            run.IdempotencyKey = idempotencyKey;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var next = await FindNextOutstandingAsync(@event, participant.SquadId, runNumber, participantId, cancellationToken);
        return new RunResult.Success(new SaveRunResponse(ToRunResponse(run), next));
    }

    /// <summary>
    /// Finishes the just-saved run number for the rest of the squad first ("work down
    /// squad 1 recording run 1"), then falls back to any outstanding run at all ("loop
    /// back for run 2") — architecture doc section A. A participant not in a squad has no
    /// "next" to advance to.
    /// </summary>
    private async Task<RunnerParticipantResponse?> FindNextOutstandingAsync(
        Event @event, Guid? squadId, int justSavedRunNumber, Guid justSavedParticipantId, CancellationToken cancellationToken)
    {
        if (squadId is null)
        {
            return null;
        }

        var participants = await dbContext.EventParticipants
            .Where(p => p.SquadId == squadId)
            .Join(dbContext.Shooters, p => p.ShooterId, s => s.Id, (p, s) => new { p, s })
            .OrderBy(x => x.p.PositionInSquad)
            .ToListAsync(cancellationToken);

        var startIndex = participants.FindIndex(x => x.p.Id == justSavedParticipantId);
        if (startIndex < 0 || participants.Count == 0)
        {
            return null;
        }

        var participantIds = participants.Select(x => x.p.Id).ToList();
        var runsByParticipant = (await dbContext.Runs
                .Where(r => participantIds.Contains(r.EventParticipantId))
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.EventParticipantId);

        bool HasOutstanding(Guid participantId, int runNumber) =>
            runsByParticipant[participantId].All(r => r.RunNumber != runNumber);

        var orderedFromNext = Enumerable.Range(1, participants.Count)
            .Select(offset => participants[(startIndex + offset) % participants.Count]);

        var sameRound = orderedFromNext.FirstOrDefault(x => HasOutstanding(x.p.Id, justSavedRunNumber));
        var match = sameRound
            ?? orderedFromNext.FirstOrDefault(x =>
                Enumerable.Range(1, @event.RunsPerShooter).Any(n => HasOutstanding(x.p.Id, n)));

        return match is null
            ? null
            : RunnerService.BuildParticipantResponse(match.p, match.s, runsByParticipant[match.p.Id], @event.RunsPerShooter);
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");

    private static RunResponse ToRunResponse(Run run) =>
        new(run.EventParticipantId, run.RunNumber, run.RawTimeMs, run.PenaltyCount, run.IsDnf, run.RecordedAt);
}
