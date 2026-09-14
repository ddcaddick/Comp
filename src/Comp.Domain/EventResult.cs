using Comp.Domain.Enums;

namespace Comp.Domain;

/// <summary>
/// Computed per participant: best run, event time, overall position, league position,
/// league points, and the rules version used. Derived live during the event, then frozen
/// at finalisation. Never a source of truth for standings — those are always a query over
/// this table, so an amendment recalculates them for free.
/// </summary>
public class EventResult
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventId { get; init; }
    public required Guid EventParticipantId { get; init; }
    public Guid? LeagueId { get; set; }
    public Guid? BestRunId { get; set; }
    public int? EventTimeMs { get; set; }
    public EventResultStatus Status { get; set; } = EventResultStatus.Dnf;
    public int? OverallPosition { get; set; }
    public int? LeaguePosition { get; set; }
    public int LeaguePoints { get; set; } = 0;
    public required int RulesVersion { get; set; }
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
}
