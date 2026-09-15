namespace Comp.Contracts.Leagues;

/// <summary>
/// <c>DroppedTotal</c> is <c>RunningTotal - CountingTotal</c>, zero while
/// <c>IsProvisional</c> is true (the drop rule hasn't started applying yet — architecture
/// doc decision D7).
/// </summary>
public record LeagueStandingResponse(
    Guid ShooterId,
    string FirstName,
    string LastName,
    string? Nickname,
    int Position,
    int RunningTotal,
    int CountingTotal,
    int DroppedTotal,
    bool IsProvisional);
