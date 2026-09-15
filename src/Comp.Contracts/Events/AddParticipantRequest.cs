namespace Comp.Contracts.Events;

public record AddParticipantRequest(Guid ShooterId, Guid? SquadId);
