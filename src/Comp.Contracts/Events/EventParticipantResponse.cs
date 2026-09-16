namespace Comp.Contracts.Events;

public record EventParticipantResponse(
    Guid Id,
    Guid ShooterId,
    string FirstName,
    string LastName,
    string? Nickname,
    Guid? LeagueId,
    Guid? SquadId,
    int? PositionInSquad,
    DateTimeOffset AddedAt);
