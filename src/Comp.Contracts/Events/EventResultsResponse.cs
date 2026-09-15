namespace Comp.Contracts.Events;

/// <summary>
/// <c>IsFinal</c> distinguishes a live, recompute-on-every-request table (the event isn't
/// finalised yet) from the frozen one read straight out of <c>event_results</c> once it is —
/// architecture doc section H: "Event results derived live, frozen at finalisation."
/// Filtered to one league (ordered by league position) when the caller passed a
/// <c>leagueId</c>; otherwise every participant, ordered by overall position with DNFs last.
/// </summary>
public record EventResultsResponse(
    Guid EventId,
    string EventStatus,
    bool IsFinal,
    IReadOnlyList<EventParticipantResultResponse> Participants);
