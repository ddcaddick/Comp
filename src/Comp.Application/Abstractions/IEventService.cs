using Comp.Contracts.Events;

namespace Comp.Application.Abstractions;

public abstract record EventCommandResult
{
    private EventCommandResult() { }

    public sealed record Success(EventResponse Event) : EventCommandResult;

    public sealed record NotFound : EventCommandResult;

    public sealed record Conflict(string Reason) : EventCommandResult;
}

public interface IEventService
{
    Task<EventCommandResult> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<EventResponse>> ListAsync(
        Guid? competitionId, string? status, CancellationToken cancellationToken);

    Task<EventCommandResult> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Moves to an adjacent status in the Draft → Setup → InProgress → Review chain, in
    /// either direction. Finalising isn't available yet — that's M8, which needs the
    /// scoring engine wired in to actually compute and freeze results; this returns a
    /// <see cref="EventCommandResult.Conflict"/> for a "to" of Finalised in the meantime, and
    /// always for a currently-finalised event (locked, full stop, until M8's amend flow).
    /// </summary>
    Task<EventCommandResult> TransitionAsync(Guid id, TransitionEventRequest request, CancellationToken cancellationToken);
}
