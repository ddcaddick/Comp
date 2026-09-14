namespace Comp.Contracts.Shooters;

public record ShooterHistoryEntryResponse(
    string Action,
    DateTimeOffset OccurredAt,
    Guid ActorUserId,
    string? Before,
    string? After,
    string? Reason);
