namespace Comp.Contracts.Leagues;

/// <summary><c>EventsHeld</c> is how many finalised, counts-for-standings events the
/// competition has had so far — drives the drop rule's D7 threshold.</summary>
public record LeagueStandingsResponse(
    Guid LeagueId,
    string LeagueName,
    int EventsHeld,
    IReadOnlyList<LeagueStandingResponse> Standings);
