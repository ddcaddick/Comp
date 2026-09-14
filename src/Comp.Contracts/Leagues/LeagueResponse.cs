namespace Comp.Contracts.Leagues;

public record LeagueResponse(
    Guid Id,
    Guid CompetitionId,
    string Name,
    int Tier,
    int PointsForFirst,
    int PointsDecrement,
    int DropWorstCount,
    bool AbsencesCountAsZero);
