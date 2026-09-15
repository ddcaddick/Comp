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

        return new EventCommandResult.Success(ToResponse(@event));
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
        return events.Select(ToResponse).ToList();
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

        return new EventCommandResult.Success(ToResponse(@event));
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

            return new EventCommandResult.Success(ToResponse(@event));
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

        return new EventCommandResult.Success(ToResponse(@event));
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

        return new EventCommandResult.Success(ToResponse(@event));
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");

    private static EventResponse ToResponse(Event @event) => new(
        @event.Id,
        @event.CompetitionId,
        @event.EventNumber,
        @event.Name,
        @event.EventDate,
        @event.Status.ToString(),
        @event.PenaltySeconds,
        @event.RunsPerShooter,
        @event.CountsForStandings,
        @event.ScoringRulesVersion);
}
