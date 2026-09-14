namespace Comp.Domain;

/// <summary>
/// One row per event, supporting detect-and-warn when a second official opens the entry
/// screen while another is already recording runs for the same event.
/// </summary>
public class EntrySession
{
    public required Guid EventId { get; init; }
    public required Guid UserId { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
