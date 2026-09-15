namespace Comp.Contracts.Events;

/// <summary>All three fields are null when no one has ever recorded a run for this event yet.</summary>
public record EntrySessionResponse(Guid? UserId, string? DisplayName, DateTimeOffset? LastSeenAt);
