namespace Comp.Contracts.Events;

public record RunnerParticipantResponse(
    Guid ParticipantId,
    Guid ShooterId,
    string FirstName,
    string LastName,
    string? Nickname,
    int? PositionInSquad,
    IReadOnlyList<RunnerRunState> Runs);
