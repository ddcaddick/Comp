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
    /// either direction, or from Review to Finalised — which computes and freezes results
    /// via <see cref="IResultsService.RecalculateAndPersistAsync"/> before locking. A
    /// currently-finalised event refuses every transition; <see cref="AmendAsync"/> is the
    /// only way out.
    /// </summary>
    Task<EventCommandResult> TransitionAsync(Guid id, TransitionEventRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Unlocks a finalised event back to Review with a mandatory reason (audited), clearing
    /// its frozen results — they're stale the moment the event reopens for correction, and
    /// a fresh finalisation recomputes them. Callers gate this on the amend-published
    /// privilege; this method doesn't check it itself.
    /// </summary>
    Task<EventCommandResult> AmendAsync(Guid id, AmendEventRequest request, CancellationToken cancellationToken);
}
