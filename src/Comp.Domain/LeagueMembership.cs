namespace Comp.Domain;

/// <summary>
/// One shooter, one league, one competition. A unique constraint on
/// (<see cref="CompetitionId"/>, <see cref="ShooterId"/>) enforces the no-mid-year-moves
/// rule in the database, not just in application code.
/// </summary>
public class LeagueMembership
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid CompetitionId { get; init; }
    public required Guid LeagueId { get; set; }
    public required Guid ShooterId { get; init; }
    public DateTimeOffset AssignedAt { get; init; } = DateTimeOffset.UtcNow;
}
