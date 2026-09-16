namespace Comp.Contracts.Events;

/// <summary>
/// One participant's row in an event's results table. <c>Status</c> is "Ranked" or "DNF" —
/// a DNF participant still appears (never a position, overall or league, and zero league
/// points), matching the architecture doc's section I.
/// </summary>
public record EventParticipantResultResponse(
    Guid ParticipantId,
    Guid ShooterId,
    string FirstName,
    string LastName,
    string? Nickname,
    Guid? LeagueId,
    string? LeagueName,
    string Status,
    int? EventTimeMs,
    int? BestRunNumber,
    int? OverallPosition,
    int? LeaguePosition,
    int LeaguePoints);
