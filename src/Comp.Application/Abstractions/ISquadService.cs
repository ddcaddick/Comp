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
}
