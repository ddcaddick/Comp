namespace Comp.Contracts.Events;

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
    int ScoringRulesVersion);
