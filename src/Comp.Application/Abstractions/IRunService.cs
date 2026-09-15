using Comp.Contracts.Events;

namespace Comp.Application.Abstractions;

public abstract record RunResult
{
    private RunResult() { }

    public sealed record Success(SaveRunResponse Response) : RunResult;

    public sealed record NotFound : RunResult;

    public sealed record Conflict(string Reason) : RunResult;
}

public interface IRunService
{
    /// <summary>
    /// Upserts the run at (participantId, runNumber). If <paramref name="idempotencyKey"/>
    /// was already used for a previous save, that save's result is replayed rather than
    /// applying the request again — see the architecture doc's idempotency guarantee.
    /// </summary>
    Task<RunResult> SaveRunAsync(
        Guid eventId,
        Guid participantId,
        int runNumber,
        SaveRunRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);
}
