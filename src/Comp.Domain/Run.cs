namespace Comp.Domain;

/// <summary>
/// The raw fact of one run: run number, raw milliseconds (null when DNF), penalty count,
/// DNF flag, who recorded it and when, and an idempotency key so a double-tap on a flaky
/// connection never records the same run twice. Runs are never deleted; a correction
/// updates the row and writes an audit record.
/// </summary>
public class Run
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid EventParticipantId { get; init; }
    public required int RunNumber { get; set; }
    public int? RawTimeMs { get; set; }
    public int PenaltyCount { get; set; } = 0;
    public bool IsDnf { get; set; } = false;
    public required Guid RecordedByUserId { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? IdempotencyKey { get; set; }
}
