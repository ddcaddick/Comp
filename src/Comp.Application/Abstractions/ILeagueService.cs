using Comp.Contracts.Leagues;

namespace Comp.Application.Abstractions;

public abstract record LeagueResult
{
    private LeagueResult() { }

    public sealed record Success(LeagueResponse League) : LeagueResult;

    public sealed record NotFound : LeagueResult;

    public sealed record Conflict(string Reason) : LeagueResult;
}

public abstract record LeagueMembersResult
{
    private LeagueMembersResult() { }

    public sealed record Success(IReadOnlyList<LeagueMemberResponse> Members) : LeagueMembersResult;

    public sealed record NotFound : LeagueMembersResult;

    public sealed record Conflict(string Reason) : LeagueMembersResult;
}

public interface ILeagueService
{
    Task<LeagueResult> CreateAsync(Guid competitionId, CreateLeagueRequest request, CancellationToken cancellationToken);

    /// <summary>Null return means the competition itself doesn't exist.</summary>
    Task<IReadOnlyList<LeagueResponse>?> ListForCompetitionAsync(Guid competitionId, CancellationToken cancellationToken);

    Task<LeagueMembersResult> GetMembersAsync(Guid leagueId, CancellationToken cancellationToken);

    /// <summary>
    /// Full-replace: shooters not in the request are removed from this league; shooters
    /// already a member of a different league in the same competition are moved rather
    /// than rejected (the no-mid-year-move rule only protects the historical snapshot on
    /// EventParticipant.LeagueId, not the current LeagueMembership row — see the
    /// architecture doc's section D).
    /// </summary>
    Task<LeagueMembersResult> SetMembersAsync(Guid leagueId, SetLeagueMembersRequest request, CancellationToken cancellationToken);
}
