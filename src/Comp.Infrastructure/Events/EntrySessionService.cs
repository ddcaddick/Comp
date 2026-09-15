using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

public class EntrySessionService(CompDbContext dbContext, ICurrentUserAccessor currentUser) : IEntrySessionService
{
    public async Task<EntrySessionResult> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventExists = await dbContext.Events.AnyAsync(e => e.Id == eventId, cancellationToken);
        if (!eventExists)
        {
            return new EntrySessionResult.NotFound();
        }

        var session = await dbContext.EntrySessions
            .Where(s => s.EventId == eventId)
            .Join(dbContext.Users, s => s.UserId, u => u.Id, (s, u) => new { s.UserId, u.DisplayName, s.LastSeenAt })
            .SingleOrDefaultAsync(cancellationToken);

        return new EntrySessionResult.Success(session is null
            ? new EntrySessionResponse(null, null, null)
            : new EntrySessionResponse(session.UserId, session.DisplayName, session.LastSeenAt));
    }

    public async Task<EntrySessionResult> HeartbeatAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventExists = await dbContext.Events.AnyAsync(e => e.Id == eventId, cancellationToken);
        if (!eventExists)
        {
            return new EntrySessionResult.NotFound();
        }

        var actorId = RequireActorId();
        var now = DateTimeOffset.UtcNow;

        var session = await dbContext.EntrySessions.SingleOrDefaultAsync(s => s.EventId == eventId, cancellationToken);
        if (session is null)
        {
            session = new EntrySession { EventId = eventId, UserId = actorId, LastSeenAt = now };
            dbContext.EntrySessions.Add(session);
        }
        else
        {
            session.UserId = actorId;
            session.LastSeenAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var displayName = await dbContext.Users
            .Where(u => u.Id == actorId)
            .Select(u => u.DisplayName)
            .SingleAsync(cancellationToken);

        return new EntrySessionResult.Success(new EntrySessionResponse(actorId, displayName, now));
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");
}
