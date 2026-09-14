using Comp.Contracts.Competitions;

namespace Comp.Application.Abstractions;

public abstract record CompetitionResult
{
    private CompetitionResult() { }

    public sealed record Success(CompetitionResponse Competition) : CompetitionResult;

    public sealed record NotFound : CompetitionResult;

    public sealed record Conflict(string Reason) : CompetitionResult;
}

public interface ICompetitionService
{
    Task<CompetitionResult> CreateAsync(CreateCompetitionRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CompetitionResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One-way: freezes standings and enables promotion. There is no "reopen" — closing an
    /// already-closed competition is a <see cref="CompetitionResult.Conflict"/>, not a no-op.
    /// </summary>
    Task<CompetitionResult> CloseAsync(Guid id, CancellationToken cancellationToken);
}
