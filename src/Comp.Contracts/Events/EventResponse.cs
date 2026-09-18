namespace Comp.Contracts.Events;

/// <summary>
/// <c>ShooterCount</c> is how many distinct shooters actually shot this event -- at least
/// one run recorded, DNF or not -- the same "shot" definition CompetitionService's
/// TotalShooters uses. A participant added but never called up doesn't count.
/// </summary>
public record EventResponse(
    Guid Id,
    Guid CompetitionId,
    int EventNumber,
    string Name,
    DateOnly EventDate,
    string Status,
    decimal PenaltySeconds,
    int RunsPerShooter,
    bool CountsForStandings,
    int ScoringRulesVersion,
    int ShooterCount);
