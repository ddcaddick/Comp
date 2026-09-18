namespace Comp.Contracts.Competitions;

/// <summary>
/// <c>TotalShooters</c> counts who has actually shot at least once (see
/// CompetitionService); <c>RosteredShooters</c> is different -- every shooter set up
/// against any of this competition's leagues, whether or not they've shot yet. A
/// shooter added to a league roster the day before the season starts counts here
/// immediately, long before they show up in TotalShooters.
/// </summary>
public record CompetitionResponse(
    Guid Id,
    string Name,
    int Year,
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Status,
    int TotalShooters,
    decimal AverageShootersPerEvent,
    int EventsRemaining,
    int RosteredShooters);
