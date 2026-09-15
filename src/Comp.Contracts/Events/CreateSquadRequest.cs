namespace Comp.Contracts.Events;

/// <summary>A null <see cref="SquadNumber"/> auto-assigns the next one after the event's current highest.</summary>
public record CreateSquadRequest(int? SquadNumber, string? Name);
