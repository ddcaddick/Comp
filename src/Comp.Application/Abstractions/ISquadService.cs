using Comp.Contracts.Events;

namespace Comp.Application.Abstractions;

public abstract record SquadResult
{
    private SquadResult() { }

    public sealed record Success(SquadResponse Squad) : SquadResult;

    public sealed record NotFound : SquadResult;

    public sealed record Conflict(string Reason) : SquadResult;
}

public interface ISquadService
{
    Task<SquadResult> CreateAsync(Guid eventId, CreateSquadRequest request, CancellationToken cancellationToken);

    /// <summary>Null return means the event itself doesn't exist.</summary>
    Task<IReadOnlyList<SquadResponse>?> ListAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Locks the squad's roster (<see cref="Comp.Domain.Enums.SquadStatus.Allocated"/>) so no
    /// further shooters can be assigned into it. Idempotent -- completing an already-allocated
    /// squad just returns it unchanged rather than conflicting.</summary>
    Task<SquadResult> CompleteAsync(Guid eventId, Guid squadId, CancellationToken cancellationToken);

    /// <summary>Undoes <see cref="CompleteAsync"/>, moving the squad back to
    /// <see cref="Comp.Domain.Enums.SquadStatus.Pending"/> so it can be assigned into again and
    /// its roster amended. Idempotent -- reopening an already-pending squad just returns it
    /// unchanged rather than conflicting.</summary>
    Task<SquadResult> ReopenAsync(Guid eventId, Guid squadId, CancellationToken cancellationToken);
}
