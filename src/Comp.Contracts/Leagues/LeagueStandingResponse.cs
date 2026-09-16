namespace Comp.Contracts.Leagues;

/// <summary>
/// <c>MissedEvents</c> is how many of the competition's finalised, counting events this
/// shooter has no real result for — a factual attendance count, unrelated to the league's
/// worst-N drop rule (which only affects <c>CountingTotal</c>).
/// </summary>
public record LeagueStandingResponse(
    Guid ShooterId,
    string FirstName,
    string LastName,
    string? Nickname,
    int Position,
    int RunningTotal,
    int CountingTotal,
    int MissedEvents,
    bool IsProvisional);
