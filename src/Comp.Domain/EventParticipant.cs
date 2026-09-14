namespace Comp.Domain;

/// <summary>
/// A shooter's entry into one event. Snapshots the league the shooter was in at the moment
/// of entry, so correcting a membership error later cannot rewrite an earlier night's
/// division tables.
/// </summary>
public class EventParticipant
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventId { get; init; }
    public required Guid ShooterId { get; init; }

    /// <summary>Snapshot of the shooter's league at entry time — deliberately not a live join.</summary>
    public Guid? LeagueId { get; set; }
    public Guid? SquadId { get; set; }
    public int? PositionInSquad { get; set; }
    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;
    public required Guid AddedByUserId { get; init; }
}
