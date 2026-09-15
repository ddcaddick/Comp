using Comp.Contracts.Events;
using Comp.Contracts.Leagues;

namespace Comp.Application.Abstractions;

public abstract record LeagueStandingsResult
{
    private LeagueStandingsResult() { }

    public sealed record Success(LeagueStandingsResponse Standings) : LeagueStandingsResult;

    public sealed record NotFound : LeagueStandingsResult;
}

/// <summary>
/// The only place <c>Comp.Scoring</c> is ever called from the API side — see the
/// architecture doc's rule that scoring exists only in that library. <see cref="IEventService"/>
/// calls <see cref="RecalculateAndPersistAsync"/> when finalising; nothing else writes to
/// <c>event_results</c>.
/// </summary>
public interface IResultsService
{
    /// <summary>Null if the event doesn't exist. Live-computed unless the event is
    /// finalised, in which case it reads the frozen <c>event_results</c> rows instead.</summary>
    Task<EventResultsResponse?> GetEventResultsAsync(Guid eventId, Guid? leagueId, CancellationToken cancellationToken);

    /// <summary>
    /// Computes the event fresh via <c>Comp.Scoring</c> and replaces its <c>event_results</c>
    /// rows with the result — safe to call repeatedly (a re-finalisation after an amendment
    /// simply overwrites the stale rows). Does not touch the event's own status; the caller
    /// (<see cref="IEventService"/>) owns the lifecycle transition around this call.
    /// </summary>
    Task RecalculateAndPersistAsync(Guid eventId, CancellationToken cancellationToken);

    Task<LeagueStandingsResult> GetLeagueStandingsAsync(Guid leagueId, CancellationToken cancellationToken);
}
