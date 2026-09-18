using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Domain;
using Comp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

public class EventService(CompDbContext dbContext, ICurrentUserAccessor currentUser, IResultsService resultsService) : IEventService
{
    // Finalising is excluded — see IEventService.TransitionAsync's remarks.
    private static readonly EventStatus[] PreFinaliseChain =
    [
        EventStatus.Draft, EventStatus.Setup, EventStatus.InProgress, EventStatus.Review
    ];

    public async Task<EventCommandResult> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions.AnyAsync(c => c.Id == request.CompetitionId, cancellationToken);
        if (!competitionExists)
        {
            return new EventCommandResult.NotFound();
        }

        // Service-level check ahead of the unique(competition_id, event_number) index.
        var duplicate = await dbContext.Events.AnyAsync(
            e => e.CompetitionId == request.CompetitionId && e.EventNumber == request.EventNumber, cancellationToken);
        if (duplicate)
        {
            return new EventCommandResult.Conflict($"Event number {request.EventNumber} already exists in this competition.");
        }

        var @event = new Event
        {
            CompetitionId = request.CompetitionId,
            EventNumber = request.EventNumber,
            Name = request.Name,
            EventDate = request.EventDate,
            PenaltySeconds = request.PenaltySeconds ?? 5.00m,
            RunsPerShooter = request.RunsPerShooter ?? 2,
            CountsForStandings = request.CountsForStandings ?? true,
            CreatedByUserId = RequireActorId()
        };
        dbContext.Events.Add(@event);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventCommandResult.Success(await ToResponseAsync(@event, cancellationToken));
    }

    public async Task<IReadOnlyList<EventResponse>> ListAsync(
        Guid? competitionId, string? status, CancellationToken cancellationToken)
    {
        var query = dbContext.Events.AsQueryable();

        if (competitionId is not null)
        {
            query = query.Where(e => e.CompetitionId == competitionId);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EventStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(e => e.Status == parsedStatus);
        }

        var events = await query.OrderBy(e => e.EventNumber).ToListAsync(cancellationToken);
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

        var shooterCountByEvent = participants
            .Where(p => shotParticipantIds.Contains(p.Id))
            .ToLookup(p => p.EventId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ShooterId).Distinct().Count());

        return events.Select(e => ToResponse(e, shooterCountByEvent.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<EventCommandResult> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([id], cancellationToken);
        if (@event is null)
        {
            return new EventCommandResult.NotFound();
        }

        if (@event.Status == EventStatus.Finalised)
        {
            return new EventCommandResult.Conflict("Cannot edit a finalised event.");
        }

        @event.Name = request.Name;
        @event.EventDate = request.EventDate;
        @event.PenaltySeconds = request.PenaltySeconds;
        @event.RunsPerShooter = request.RunsPerShooter;
        @event.CountsForStandings = request.CountsForStandings;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventCommandResult.Success(await ToResponseAsync(@event, cancellationToken));
    }

    public async Task<EventCommandResult> TransitionAsync(
        Guid id, TransitionEventRequest request, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([id], cancellationToken);
        if (@event is null)
        {
            return new EventCommandResult.NotFound();
        }

        if (!Enum.TryParse<EventStatus>(request.To, ignoreCase: true, out var target))
        {
            return new EventCommandResult.Conflict($"'{request.To}' is not a valid event status.");
        }

        if (@event.Status == EventStatus.Finalised)
        {
            return new EventCommandResult.Conflict("Event is finalised and locked; use the amend endpoint to unlock it.");
        }

        if (target == EventStatus.Finalised)
        {
            if (@event.Status != EventStatus.Review)
            {
                return new EventCommandResult.Conflict("An event can only be finalised from Review.");
            }

            // Computes and freezes event_results before the status flips — RecalculateAndPersistAsync
            // saves its own changes, so this is two SaveChanges calls (results, then the status
            // itself), each its own audit entry.
            await resultsService.RecalculateAndPersistAsync(id, cancellationToken);
            @event.Status = EventStatus.Finalised;
            @event.FinalisedAt = DateTimeOffset.UtcNow;
            @event.FinalisedByUserId = RequireActorId();
            await dbContext.SaveChangesAsync(cancellationToken);

            return new EventCommandResult.Success(await ToResponseAsync(@event, cancellationToken));
        }

        var currentIndex = Array.IndexOf(PreFinaliseChain, @event.Status);
        var targetIndex = Array.IndexOf(PreFinaliseChain, target);
        if (Math.Abs(currentIndex - targetIndex) != 1)
        {
            return new EventCommandResult.Conflict(
                $"Cannot move directly from {@event.Status} to {target}; only one step at a time is allowed.");
        }

        @event.Status = target;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventCommandResult.Success(await ToResponseAsync(@event, cancellationToken));
    }

    public async Task<EventCommandResult> AmendAsync(Guid id, AmendEventRequest request, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([id], cancellationToken);
        if (@event is null)
        {
            return new EventCommandResult.NotFound();
        }

        if (@event.Status != EventStatus.Finalised)
        {
            return new EventCommandResult.Conflict("Only a finalised event can be amended.");
        }

        // The frozen results are stale the moment the event reopens — a fresh finalisation
        // recomputes them from whatever the correction turns out to be.
        var staleResults = await dbContext.EventResults.Where(r => r.EventId == id).ToListAsync(cancellationToken);
        dbContext.EventResults.RemoveRange(staleResults);

        @event.Status = EventStatus.Review;
        @event.FinalisedAt = null;
        @event.FinalisedByUserId = null;
        dbContext.PendingAuditReason = request.Reason;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventCommandResult.Success(await ToResponseAsync(@event, cancellationToken));
    }

    public async Task<EventCommandResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([id], cancellationToken);
        if (@event is null)
        {
            return new EventCommandResult.NotFound();
        }

        var participantIds = await dbContext.EventParticipants
            .Where(p => p.EventId == id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        // Every one of these FKs is Restrict (see the relevant *Configuration classes), so a
        // plain Events.Remove would fail at the database. EF Core topologically sorts pending
        // deletes by the model's own FK graph within one SaveChangesAsync, so the removes below
        // don't need to be sequenced by hand or split across multiple save calls.
        var runs = await dbContext.Runs.Where(r => participantIds.Contains(r.EventParticipantId)).ToListAsync(cancellationToken);
        var results = await dbContext.EventResults.Where(r => r.EventId == id).ToListAsync(cancellationToken);
        var participants = await dbContext.EventParticipants.Where(p => p.EventId == id).ToListAsync(cancellationToken);
        var squads = await dbContext.Squads.Where(s => s.EventId == id).ToListAsync(cancellationToken);
        var entrySession = await dbContext.EntrySessions.SingleOrDefaultAsync(s => s.EventId == id, cancellationToken);

        dbContext.Runs.RemoveRange(runs);
        dbContext.EventResults.RemoveRange(results);
        dbContext.EventParticipants.RemoveRange(participants);
        dbContext.Squads.RemoveRange(squads);
        if (entrySession is not null)
        {
            dbContext.EntrySessions.Remove(entrySession);
        }
        dbContext.Events.Remove(@event);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventCommandResult.Removed();
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");

    // Used by every single-event mutation, which each handle one event -- ListAsync has its
    // own batched version of this same query shape to avoid N+1 round trips.
    private async Task<EventResponse> ToResponseAsync(Event @event, CancellationToken cancellationToken)
    {
        var participantIds = await dbContext.EventParticipants
            .Where(p => p.EventId == @event.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var shooterCount = await dbContext.EventParticipants
            .Where(p => participantIds.Contains(p.Id) && dbContext.Runs.Any(r => r.EventParticipantId == p.Id))
            .Select(p => p.ShooterId)
            .Distinct()
            .CountAsync(cancellationToken);

        return ToResponse(@event, shooterCount);
    }

    private static EventResponse ToResponse(Event @event, int shooterCount) => new(
        @event.Id,
        @event.CompetitionId,
        @event.EventNumber,
        @event.Name,
        @event.EventDate,
        @event.Status.ToString(),
        @event.PenaltySeconds,
        @event.RunsPerShooter,
        @event.CountsForStandings,
        @event.ScoringRulesVersion,
        shooterCount);
}
