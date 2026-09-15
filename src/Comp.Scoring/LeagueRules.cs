namespace Comp.Scoring;

/// <summary>
/// One league's scoring configuration — used both while scoring an event (points) and
/// while calculating standings (the drop rule and absence handling).
/// </summary>
public readonly record struct LeagueRules(
    int PointsForFirst,
    int PointsDecrement,
    int DropWorstCount,
    bool AbsencesCountAsZero);
