using Comp.Contracts.Events;

namespace Comp.Application.Abstractions;

public interface IRunnerService
{
    /// <summary>Null return means the event or the squad (or the squad not belonging to this event) doesn't exist.</summary>
    Task<SquadRunnerResponse?> GetAsync(Guid eventId, Guid squadId, CancellationToken cancellationToken);
}
