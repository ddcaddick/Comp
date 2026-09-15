namespace Comp.Scoring;

/// <summary>
/// One shooter's points from each counting event they actually attended, in
/// chronological order — missed events are simply absent from this list, not represented
/// as an explicit zero. <see cref="StandingsCalculator"/> decides how an absence affects
/// the total, per <see cref="LeagueRules.AbsencesCountAsZero"/>, so that logic lives in
/// one place rather than being duplicated by every caller.
/// </summary>
public readonly record struct ShooterEventPoints(Guid ShooterId, IReadOnlyList<int> EventPoints);
