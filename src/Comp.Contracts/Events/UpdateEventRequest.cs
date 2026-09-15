namespace Comp.Contracts.Events;

public record UpdateEventRequest(
    string Name,
    DateOnly EventDate,
    decimal PenaltySeconds,
    int RunsPerShooter,
    bool CountsForStandings);
