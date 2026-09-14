using Comp.Domain.Enums;

namespace Comp.Domain;

/// <summary>
/// An ordered group created on the day. Membership lives on <see cref="EventParticipant"/>
/// rather than here, so a shooter can be moved between squads mid-event.
/// </summary>
public class Squad
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventId { get; init; }
    public required int SquadNumber { get; set; }
    public string? Name { get; set; }
    public SquadStatus Status { get; set; } = SquadStatus.Pending;
}
