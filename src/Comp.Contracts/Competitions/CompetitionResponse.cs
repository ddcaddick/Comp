namespace Comp.Contracts.Competitions;

public record CompetitionResponse(
    Guid Id,
    string Name,
    int Year,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    int TotalShooters,
    decimal AverageShootersPerEvent,
    int EventsRemaining);
