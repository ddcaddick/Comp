using System.Text.Json;
using Comp.Application.Abstractions;
using Comp.Domain;
using Comp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Comp.Infrastructure;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every tracked create, update and delete on
/// <c>SaveChanges</c>, with the actor taken from <see cref="ICurrentUserAccessor"/>. This
/// is the only place audit rows are produced — nothing should insert into audit_log
/// directly or bypass this with raw SQL or <c>ExecuteUpdate</c> for entity changes.
/// </summary>
public class AuditSaveChangesInterceptor(ICurrentUserAccessor currentUser) : SaveChangesInterceptor
{
    // Operational tables, not "competition data" or a reviewable admin action:
    // - entry_sessions is a heartbeat that would spam the log on every request.
    // - refresh_tokens churns on every token refresh.
    // - the Identity join/token tables have no single "Id" this interceptor can resolve,
    //   and role/claim assignment is rare enough to audit explicitly at the call site
    //   instead. AppUser itself IS audited — creating, editing or granting
    //   CanAmendPublished on a user is exactly the kind of write this exists for.
    // - audit_log must never audit its own inserts.
    private static readonly HashSet<Type> Excluded =
    [
        typeof(AuditLog),
        typeof(EntrySession),
        typeof(RefreshToken),
        typeof(IdentityRole<Guid>),
        typeof(IdentityUserRole<Guid>),
        typeof(IdentityUserClaim<Guid>),
        typeof(IdentityUserLogin<Guid>),
        typeof(IdentityUserToken<Guid>),
        typeof(IdentityRoleClaim<Guid>)
    ];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is CompDbContext context)
        {
            AddAuditEntries(context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is CompDbContext context)
        {
            AddAuditEntries(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(CompDbContext context)
    {
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => !Excluded.Contains(e.Entity.GetType()))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        // The override (set by a flow with no ClaimsPrincipal yet, e.g. a login attempt
        // updating the user's own lockout counters) wins when present; otherwise the actor
        // must come from the authenticated request.
        var actorUserId = context.PendingActorOverride ?? currentUser.UserId
            ?? throw new InvalidOperationException(
                "Cannot save a change to audited data without a current user to attribute it to.");
        context.PendingActorOverride = null;

        // Read once and clear immediately: a reason attaches only to the save it was set for.
        var reason = context.PendingAuditReason;
        context.PendingAuditReason = null;

        foreach (var entry in entries)
        {
            context.AuditLogs.Add(new AuditLog
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = ResolveEntityId(entry),
                Action = ResolveAction(entry.State),
                ActorUserId = actorUserId,
                Before = SerializeBefore(entry),
                After = SerializeAfter(entry),
                Reason = reason
            });
        }
    }

    private static Guid ResolveEntityId(EntityEntry entry) =>
        entry.Property("Id").CurrentValue as Guid? ?? Guid.Empty;

    private static string ResolveAction(EntityState state) => state switch
    {
        EntityState.Added => "Created",
        EntityState.Modified => "Updated",
        EntityState.Deleted => "Deleted",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static string? SerializeBefore(EntityEntry entry) =>
        entry.State == EntityState.Added ? null : Serialize(entry, p => p.OriginalValue);

    private static string? SerializeAfter(EntityEntry entry) =>
        entry.State == EntityState.Deleted ? null : Serialize(entry, p => p.CurrentValue);

    // Never written to audit_log regardless of entity type: AppUser carries these, and a
    // password hash or security stamp has no business sitting in a history table that
    // exists to be read by admins.
    private static readonly HashSet<string> SensitiveProperties = ["PasswordHash", "SecurityStamp"];

    /// <summary>
    /// On update, only the changed properties are captured, so before/after read as a
    /// diff rather than a full snapshot of an entity that may gain fields over time.
    /// </summary>
    private static string Serialize(EntityEntry entry, Func<PropertyEntry, object?> selector)
    {
        var properties = entry.State == EntityState.Modified
            ? entry.Properties.Where(p => p.IsModified)
            : entry.Properties;

        var values = properties
            .Where(p => !SensitiveProperties.Contains(p.Metadata.Name))
            .ToDictionary(p => p.Metadata.Name, selector);
        return JsonSerializer.Serialize(values);
    }
}
