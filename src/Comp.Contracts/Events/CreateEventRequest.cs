namespace Comp.Contracts.Events;

public record CreateEventRequest(
    Guid CompetitionId,
    int EventNumber,
    string Name,
    DateOnly EventDate,
    decimal? PenaltySeconds,
    int? RunsPerShooter,
    bool? CountsForStandings);
