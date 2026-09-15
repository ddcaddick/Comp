namespace Comp.Contracts.Events;

public record RunResponse(
    Guid EventParticipantId,
    int RunNumber,
    int? RawTimeMs,
    int PenaltyCount,
    bool IsDnf,
    DateTimeOffset RecordedAt);
