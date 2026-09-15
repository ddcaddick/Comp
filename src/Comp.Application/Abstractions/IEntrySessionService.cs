using Comp.Contracts.Events;

namespace Comp.Application.Abstractions;

public abstract record EntrySessionResult
{
    private EntrySessionResult() { }

    public sealed record Success(EntrySessionResponse Session) : EntrySessionResult;

    public sealed record NotFound : EntrySessionResult;
}

/// <summary>Supports the mobile app's detect-and-warn when a second official opens the same event.</summary>
public interface IEntrySessionService
{
    Task<EntrySessionResult> GetAsync(Guid eventId, CancellationToken cancellationToken);

    Task<EntrySessionResult> HeartbeatAsync(Guid eventId, CancellationToken cancellationToken);
}
