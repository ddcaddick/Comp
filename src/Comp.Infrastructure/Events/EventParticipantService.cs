using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Domain;
using Comp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

public class EventParticipantService(CompDbContext dbContext, ICurrentUserAccessor currentUser) : IEventParticipantService
{
    public async Task<IReadOnlyList<EventParticipantResponse>?> ListAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventExists = await dbContext.Events.AnyAsync(e => e.Id == eventId, cancellationToken);
        if (!eventExists)
        {
            return null;
        }

        return await dbContext.EventParticipants
            .Where(p => p.EventId == eventId)
            .Join(dbContext.Shooters, p => p.ShooterId, s => s.Id, (p, s) => new { p, s })
            .OrderBy(x => x.p.SquadId).ThenBy(x => x.p.PositionInSquad)
            .Select(x => new EventParticipantResponse(
                x.p.Id, x.s.Id, x.s.FirstName, x.s.LastName, x.s.Nickname, x.p.LeagueId, x.p.SquadId, x.p.PositionInSquad, x.p.AddedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<EventParticipantResult> AddAsync(
        Guid eventId, AddParticipantRequest request, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event is null)
        {
            return new EventParticipantResult.NotFound();
        }

        if (@event.Status == EventStatus.Finalised)
        {
            return new EventParticipantResult.Conflict("Cannot change participants on a finalised event.");
        }

        var shooter = await dbContext.Shooters.FindAsync([request.ShooterId], cancellationToken);
        if (shooter is null)
        {
            return new EventParticipantResult.Conflict("Shooter does not exist.");
        }

        if (!shooter.IsActive)
        {
            return new EventParticipantResult.Conflict("Shooter is deactivated.");
        }

        var alreadyEntered = await dbContext.EventParticipants
            .AnyAsync(p => p.EventId == eventId && p.ShooterId == request.ShooterId, cancellationToken);
        if (alreadyEntered)
        {
            return new EventParticipantResult.Conflict("Shooter is already entered in this event.");
        }

        int? positionInSquad = null;
        if (request.SquadId is not null)
        {
            var squad = await dbContext.Squads
                .SingleOrDefaultAsync(s => s.Id == request.SquadId && s.EventId == eventId, cancellationToken);
            if (squad is null)
            {
                return new EventParticipantResult.Conflict("Squad does not belong to this event.");
            }

            if (squad.Status == SquadStatus.Allocated)
            {
                return new EventParticipantResult.Conflict("This squad is already allocated and closed to further additions.");
            }

            positionInSquad = await NextPositionInSquadAsync(request.SquadId.Value, cancellationToken);
        }

        // The snapshot: taken once, here, and never re-joined live — see the architecture
        // doc's section D on why a later membership correction must not rewrite this.
        var leagueId = await dbContext.LeagueMemberships
            .Where(m => m.CompetitionId == @event.CompetitionId && m.ShooterId == request.ShooterId)
            .Select(m => (Guid?)m.LeagueId)
            .SingleOrDefaultAsync(cancellationToken);

        var participant = new EventParticipant
        {
            EventId = eventId,
            ShooterId = request.ShooterId,
            LeagueId = leagueId,
            SquadId = request.SquadId,
            PositionInSquad = positionInSquad,
            AddedByUserId = RequireActorId()
        };
        dbContext.EventParticipants.Add(participant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventParticipantResult.Success(ToResponse(participant, shooter));
    }

    public async Task<EventParticipantResult> UpdateAsync(
        Guid eventId, Guid participantId, UpdateParticipantRequest request, CancellationToken cancellationToken)
    {
        var participant = await dbContext.EventParticipants
            .SingleOrDefaultAsync(p => p.Id == participantId && p.EventId == eventId, cancellationToken);
        if (participant is null)
        {
            return new EventParticipantResult.NotFound();
        }

        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event!.Status == EventStatus.Finalised)
        {
            return new EventParticipantResult.Conflict("Cannot change participants on a finalised event.");
        }

        if (request.SquadId is null)
        {
            participant.SquadId = null;
            participant.PositionInSquad = null;
        }
        else
        {
            var squad = await dbContext.Squads
                .SingleOrDefaultAsync(s => s.Id == request.SquadId && s.EventId == eventId, cancellationToken);
            if (squad is null)
            {
                return new EventParticipantResult.Conflict("Squad does not belong to this event.");
            }

            // A squad already at Allocated has had sign-on closed off deliberately -- moving
            // someone into it later would silently reopen a roster an official just locked.
            if (squad.Status == SquadStatus.Allocated && participant.SquadId != squad.Id)
            {
                return new EventParticipantResult.Conflict("This squad is already allocated and closed to further additions.");
            }

            participant.SquadId = request.SquadId;
            participant.PositionInSquad = request.PositionInSquad
                ?? await NextPositionInSquadAsync(request.SquadId.Value, cancellationToken, excludeParticipantId: participant.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var shooter = await dbContext.Shooters.SingleAsync(s => s.Id == participant.ShooterId, cancellationToken);
        return new EventParticipantResult.Success(ToResponse(participant, shooter));
    }

    public async Task<EventParticipantResult> RemoveAsync(
        Guid eventId, Guid participantId, CancellationToken cancellationToken)
    {
        var participant = await dbContext.EventParticipants
            .SingleOrDefaultAsync(p => p.Id == participantId && p.EventId == eventId, cancellationToken);
        if (participant is null)
        {
            return new EventParticipantResult.NotFound();
        }

        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event!.Status == EventStatus.Finalised)
        {
            return new EventParticipantResult.Conflict("Cannot change participants on a finalised event.");
        }

        // The FK from runs to event_participants is Restrict, so the database would
        // refuse this anyway once runs exist — checked here first for a clear error
        // instead of a raw constraint-violation 500.
        var hasRuns = await dbContext.Runs.AnyAsync(r => r.EventParticipantId == participantId, cancellationToken);
        if (hasRuns)
        {
            return new EventParticipantResult.Conflict("Cannot remove a participant who already has recorded runs.");
        }

        dbContext.EventParticipants.Remove(participant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EventParticipantResult.Removed();
    }

    private async Task<int> NextPositionInSquadAsync(
        Guid squadId, CancellationToken cancellationToken, Guid? excludeParticipantId = null)
    {
        var query = dbContext.EventParticipants.Where(p => p.SquadId == squadId);
        if (excludeParticipantId is not null)
        {
            query = query.Where(p => p.Id != excludeParticipantId);
        }

        var maxPosition = await query.MaxAsync(p => (int?)p.PositionInSquad, cancellationToken);
        return (maxPosition ?? 0) + 1;
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");

    private static EventParticipantResponse ToResponse(EventParticipant participant, Shooter shooter) => new(
        participant.Id,
        shooter.Id,
        shooter.FirstName,
        shooter.LastName,
        shooter.Nickname,
        participant.LeagueId,
        participant.SquadId,
        participant.PositionInSquad,
        participant.AddedAt);
}
