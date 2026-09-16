namespace Comp.Contracts.Leagues;

/// <summary><c>EventsHeld</c> is how many finalised, counts-for-standings events the
/// competition has had so far — drives the drop rule's D7 threshold. <c>DropWorstCount</c>
/// is this league's own configured N, included so a client can show "your lowest N scores
/// are removed" without a second request.</summary>
public record LeagueStandingsResponse(
    Guid LeagueId,
    string LeagueName,
    int EventsHeld,
    int DropWorstCount,
    IReadOnlyList<LeagueStandingResponse> Standings);
