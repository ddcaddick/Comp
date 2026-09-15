namespace Comp.Contracts.Events;

public record SquadResponse(Guid Id, int SquadNumber, string? Name, string Status);
