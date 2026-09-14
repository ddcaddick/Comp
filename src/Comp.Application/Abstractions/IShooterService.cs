using Comp.Contracts.Shooters;

namespace Comp.Application.Abstractions;

public abstract record ShooterResult
{
    private ShooterResult() { }

    public sealed record Success(ShooterResponse Shooter) : ShooterResult;

    public sealed record NotFound : ShooterResult;
}

public interface IShooterService
{
    Task<ShooterResponse> CreateAsync(CreateShooterRequest request, CancellationToken cancellationToken);

    Task<ShooterResult> UpdateAsync(Guid id, UpdateShooterRequest request, CancellationToken cancellationToken);

    Task<ShooterResult> DeactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<ShooterResult> ReactivateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Deliberately unfiltered by default: <paramref name="active"/> is null unless the
    /// caller asks for it, because only the participant-selection workflow should ever
    /// filter out deactivated shooters (see docs/m2-wiring.md, "what is deliberately
    /// absent") — the general register view needs to see them too.
    /// </summary>
    Task<IReadOnlyList<ShooterResponse>> SearchAsync(
        string? query, bool? active, bool recentFirst, CancellationToken cancellationToken);

    /// <summary>Null return means no shooter with that id exists at all.</summary>
    Task<IReadOnlyList<ShooterHistoryEntryResponse>?> GetHistoryAsync(Guid id, CancellationToken cancellationToken);
}
