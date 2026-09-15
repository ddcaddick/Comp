namespace Comp.Contracts.Events;

/// <summary>
/// The entry screen's only source of truth (architecture doc section G) — the app never
/// keeps its own idea of progress, which is what makes resuming on a second phone work
/// with no dedicated code.
/// </summary>
public record SquadRunnerResponse(
    Guid SquadId,
    int SquadNumber,
    string? SquadName,
    string SquadStatus,
    IReadOnlyList<RunnerParticipantResponse> Participants);
