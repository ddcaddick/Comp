using Comp.Contracts.Events;

namespace Comp.Application.Abstractions;

public abstract record EventParticipantResult
{
    private EventParticipantResult() { }

    public sealed record Success(EventParticipantResponse Participant) : EventParticipantResult;

    public sealed record Removed : EventParticipantResult;

    public sealed record NotFound : EventParticipantResult;

    public sealed record Conflict(string Reason) : EventParticipantResult;
}

public interface IEventParticipantService
{
    /// <summary>Null return means the event itself doesn't exist.</summary>
    Task<IReadOnlyList<EventParticipantResponse>?> ListAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Snapshots the shooter's current league membership for the event's competition (if
    /// any) into the new participant's <c>LeagueId</c> — a live join is never taken later.
    /// </summary>
    Task<EventParticipantResult> AddAsync(
        Guid eventId, AddParticipantRequest request, CancellationToken cancellationToken);

    Task<EventParticipantResult> UpdateAsync(
        Guid eventId, Guid participantId, UpdateParticipantRequest request, CancellationToken cancellationToken);

    Task<EventParticipantResult> RemoveAsync(
        Guid eventId, Guid participantId, CancellationToken cancellationToken);
}
