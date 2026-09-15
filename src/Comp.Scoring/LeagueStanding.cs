namespace Comp.Scoring;

/// <summary>
/// <see cref="DroppedTotal"/> is <c>RunningTotal - CountingTotal</c> — the sum of whichever
/// worst entries were excluded, zero while <see cref="IsProvisional"/> is true (nothing
/// has been dropped yet).
/// </summary>
public readonly record struct LeagueStanding(
    Guid ShooterId,
    int RunningTotal,
    int CountingTotal,
    int DroppedTotal,
    bool IsProvisional,
    int Position);
