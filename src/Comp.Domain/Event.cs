using Comp.Domain.Enums;

namespace Comp.Domain;

/// <summary>
/// One weekly night. Belongs to one competition and scores for every league in it. Holds
/// that night's penalty seconds, the counts-for-standings flag, and the scoring rules
/// version used, so a future rule change never rewrites how this night resolved.
/// </summary>
public class Event
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid CompetitionId { get; init; }
    public required int EventNumber { get; set; }
    public required string Name { get; set; }
    public required DateOnly EventDate { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public decimal PenaltySeconds { get; set; } = 5.00m;
    public int RunsPerShooter { get; set; } = 2;
    public bool CountsForStandings { get; set; } = true;
    public int ScoringRulesVersion { get; set; } = 1;
    public DateTimeOffset? FinalisedAt { get; set; }
    public Guid? FinalisedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public required Guid CreatedByUserId { get; init; }
}
