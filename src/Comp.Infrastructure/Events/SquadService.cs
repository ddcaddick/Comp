using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Domain;
using Comp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

public class SquadService(CompDbContext dbContext) : ISquadService
{
    public async Task<SquadResult> CreateAsync(Guid eventId, CreateSquadRequest request, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event is null)
        {
            return new SquadResult.NotFound();
        }

        if (@event.Status == EventStatus.Finalised)
        {
            return new SquadResult.Conflict("Cannot add a squad to a finalised event.");
        }

        int squadNumber;
        if (request.SquadNumber is not null)
        {
            // Service-level check ahead of the unique(event_id, squad_number) index.
            var duplicate = await dbContext.Squads
                .AnyAsync(s => s.EventId == eventId && s.SquadNumber == request.SquadNumber, cancellationToken);
            if (duplicate)
            {
                return new SquadResult.Conflict($"Squad number {request.SquadNumber} already exists in this event.");
            }

            squadNumber = request.SquadNumber.Value;
        }
        else
        {
            // "Queue further squads ahead": omitting a number just appends the next one.
            var maxNumber = await dbContext.Squads
                .Where(s => s.EventId == eventId)
                .MaxAsync(s => (int?)s.SquadNumber, cancellationToken);
            squadNumber = (maxNumber ?? 0) + 1;
        }

        var squad = new Squad
        {
            EventId = eventId,
            SquadNumber = squadNumber,
            Name = request.Name
        };
        dbContext.Squads.Add(squad);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SquadResult.Success(ToResponse(squad));
    }

    public async Task<IReadOnlyList<SquadResponse>?> ListAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventExists = await dbContext.Events.AnyAsync(e => e.Id == eventId, cancellationToken);
        if (!eventExists)
        {
            return null;
        }

        var squads = await dbContext.Squads
            .Where(s => s.EventId == eventId)
            .OrderBy(s => s.SquadNumber)
            .ToListAsync(cancellationToken);

        return squads.Select(ToResponse).ToList();
    }

    private static SquadResponse ToResponse(Squad squad) =>
        new(squad.Id, squad.SquadNumber, squad.Name, squad.Status.ToString());
}
