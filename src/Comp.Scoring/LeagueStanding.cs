namespace Comp.Scoring;

/// <summary>
/// <see cref="MissedEvents"/> is a factual attendance count (how many finalised, counting
/// events this shooter has no real result for), not a reflection of the worst-N drop rule
/// — see <see cref="ShooterEventPoints.EventsMissed"/>. <c>RunningTotal - CountingTotal</c>
/// (the points actually excluded by the drop rule) is no longer surfaced here: the two
/// "drop" concepts are unrelated, and only <see cref="CountingTotal"/> itself matters once
/// the worst-N points have been excluded from it.
/// </summary>
public readonly record struct LeagueStanding(
    Guid ShooterId,
    int RunningTotal,
    int CountingTotal,
    int MissedEvents,
    bool IsProvisional,
    int Position);
