namespace Comp.Contracts.Events;

public record EventParticipantResponse(
    Guid Id,
    Guid ShooterId,
    string FirstName,
    string LastName,
    Guid? LeagueId,
    Guid? SquadId,
    int? PositionInSquad);
