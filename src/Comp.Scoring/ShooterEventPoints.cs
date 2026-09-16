namespace Comp.Scoring;

/// <summary>
/// One shooter's points from each counting event they actually attended, in
/// chronological order — missed events are simply absent from this list, not represented
/// as an explicit zero. <see cref="StandingsCalculator"/> decides how an absence affects
/// the total, per <see cref="LeagueRules.AbsencesCountAsZero"/>, so that logic lives in
/// one place rather than being duplicated by every caller.
///
/// <see cref="EventsMissed"/> is a separate, factual attendance count — how many of the
/// competition's finalised, counting events this shooter has no real result for (no run
/// recorded at all, or never entered as a participant). It is supplied by the caller
/// (which has the raw per-event status) rather than derived from <see cref="EventPoints"/>,
/// and it is entirely independent of <see cref="LeagueRules.AbsencesCountAsZero"/> or the
/// worst-N drop rule — it answers "how many did they miss," not "how many hurt their
/// total."
/// </summary>
public readonly record struct ShooterEventPoints(Guid ShooterId, IReadOnlyList<int> EventPoints, int EventsMissed);
