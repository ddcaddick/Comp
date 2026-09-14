namespace Comp.Contracts.Competitions;

public record CreateCompetitionRequest(string Name, int Year, DateOnly StartsOn, DateOnly EndsOn);
