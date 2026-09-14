namespace Comp.Contracts.Leagues;

public record CreateLeagueRequest(
    string Name,
    int Tier,
    int? PointsForFirst,
    int? PointsDecrement,
    int? DropWorstCount,
    bool? AbsencesCountAsZero);
