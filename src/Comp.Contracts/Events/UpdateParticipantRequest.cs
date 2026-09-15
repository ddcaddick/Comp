namespace Comp.Contracts.Events;

/// <summary>
/// A full replace of the participant's squad assignment, not a partial patch: a null
/// <see cref="SquadId"/> unassigns them (position is ignored in that case); a null
/// <see cref="PositionInSquad"/> with a squad given appends them to the end of it.
/// </summary>
public record UpdateParticipantRequest(Guid? SquadId, int? PositionInSquad);
