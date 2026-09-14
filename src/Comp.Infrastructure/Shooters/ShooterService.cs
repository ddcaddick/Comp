using Comp.Application.Abstractions;
using Comp.Contracts.Shooters;
using Comp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Shooters;

public class ShooterService(CompDbContext dbContext, ICurrentUserAccessor currentUser) : IShooterService
{
    private const int SearchResultLimit = 20;

    public async Task<ShooterResponse> CreateAsync(CreateShooterRequest request, CancellationToken cancellationToken)
    {
        var shooter = new Shooter
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Nickname = request.Nickname,
            MembershipNo = request.MembershipNo,
            CreatedByUserId = RequireActorId()
        };
        dbContext.Shooters.Add(shooter);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(shooter);
    }

    public async Task<ShooterResult> UpdateAsync(Guid id, UpdateShooterRequest request, CancellationToken cancellationToken)
    {
        var shooter = await dbContext.Shooters.FindAsync([id], cancellationToken);
        if (shooter is null)
        {
            return new ShooterResult.NotFound();
        }

        shooter.FirstName = request.FirstName;
        shooter.LastName = request.LastName;
        shooter.Nickname = request.Nickname;
        shooter.MembershipNo = request.MembershipNo;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ShooterResult.Success(ToResponse(shooter));
    }

    public Task<ShooterResult> DeactivateAsync(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: false, cancellationToken);

    public Task<ShooterResult> ReactivateAsync(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: true, cancellationToken);

    public async Task<IReadOnlyList<ShooterResponse>> SearchAsync(
        string? query, bool? active, bool recentFirst, CancellationToken cancellationToken)
    {
        var shooters = dbContext.Shooters.AsQueryable();

        if (active is not null)
        {
            var wantActive = active.Value;
            shooters = shooters.Where(s => s.IsActive == wantActive);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Matches the expression ix_shooters_search is built on (see the InitialSchema
            // migration's hand-added trigram index) so Postgres can use it here.
            var pattern = $"%{query}%";
            shooters = shooters.Where(s =>
                EF.Functions.ILike(s.FirstName + " " + s.LastName + " " + (s.Nickname ?? ""), pattern));
        }

        // "Recent" here means recently registered. Once event participation exists, the
        // recent-first type-ahead should probably order by last event entry instead —
        // revisit when M6 (events, participants, squads) lands.
        shooters = recentFirst
            ? shooters.OrderByDescending(s => s.CreatedAt)
            : shooters.OrderBy(s => s.LastName).ThenBy(s => s.FirstName);

        var results = await shooters.Take(SearchResultLimit).ToListAsync(cancellationToken);
        return results.Select(ToResponse).ToList();
    }

    public async Task<IReadOnlyList<ShooterHistoryEntryResponse>?> GetHistoryAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Shooters.AnyAsync(s => s.Id == id, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await dbContext.AuditLogs
            .Where(a => a.EntityType == nameof(Shooter) && a.EntityId == id)
            .OrderByDescending(a => a.OccurredAt)
            .Select(a => new ShooterHistoryEntryResponse(a.Action, a.OccurredAt, a.ActorUserId, a.Before, a.After, a.Reason))
            .ToListAsync(cancellationToken);
    }

    private async Task<ShooterResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var shooter = await dbContext.Shooters.FindAsync([id], cancellationToken);
        if (shooter is null)
        {
            return new ShooterResult.NotFound();
        }

        // Idempotent: reactivating an already-active shooter (or vice versa) is a no-op,
        // not an error, and shouldn't add a no-op row to the audit trail.
        if (shooter.IsActive != isActive)
        {
            shooter.IsActive = isActive;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ShooterResult.Success(ToResponse(shooter));
    }

    private Guid RequireActorId() =>
        currentUser.UserId ?? throw new InvalidOperationException("No authenticated user for this write.");

    private static ShooterResponse ToResponse(Shooter shooter) => new(
        shooter.Id,
        shooter.FirstName,
        shooter.LastName,
        shooter.Nickname,
        shooter.MembershipNo,
        shooter.IsActive,
        shooter.CreatedAt);
}
