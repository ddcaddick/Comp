namespace Comp.Scoring;

/// <summary>
/// One participant's outcome. <see cref="BestRunNumber"/> identifies the counting run by
/// its <c>RunNumber</c> — this library has no concept of a database row id, so the caller
/// maps back to the actual <c>Run</c> entity via (ParticipantId, BestRunNumber).
/// <see cref="LeaguePosition"/> and <see cref="LeaguePoints"/> are null/0 for a
/// participant with no league (never assigned) as well as for a DNF.
/// </summary>
public readonly record struct ParticipantScore(
    Guid ParticipantId,
    Guid? LeagueId,
    ParticipantScoringStatus Status,
    int? EventTimeMs,
    int? BestRunNumber,
    int? OverallPosition,
    int? LeaguePosition,
    int LeaguePoints);
