namespace Comp.Domain;

/// <summary>
/// Append-only. One row per create, update or delete on an audited entity: what changed,
/// who did it, when, and — for an amendment — why. No update or delete path is ever
/// exposed for this entity anywhere in the API.
/// </summary>
public class AuditLog
{
    public long Id { get; init; }
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public required string Action { get; init; }
    public required Guid ActorUserId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>JSON snapshot before the change, or null on create.</summary>
    public string? Before { get; init; }

    /// <summary>JSON snapshot after the change, or null on delete.</summary>
    public string? After { get; init; }

    /// <summary>Mandatory on an amendment to a finalised event; otherwise null.</summary>
    public string? Reason { get; init; }
}
