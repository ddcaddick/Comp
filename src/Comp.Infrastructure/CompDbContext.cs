using Comp.Domain;
using Comp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure;

public class CompDbContext(DbContextOptions<CompDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Shooter> Shooters => Set<Shooter>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Squad> Squads => Set<Squad>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<EventResult> EventResults => Set<EventResult>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EntrySession> EntrySessions => Set<EntrySession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Set by an amendment use case immediately before calling <c>SaveChangesAsync</c>, so
    /// <see cref="AuditSaveChangesInterceptor"/> can attach the mandatory reason to every
    /// row written in that call. The interceptor clears it once read, so it never leaks
    /// into an unrelated later save.
    /// </summary>
    public string? PendingAuditReason { get; set; }

    /// <summary>
    /// Overrides the actor <see cref="AuditSaveChangesInterceptor"/> attributes the next
    /// save to, for the rare flow where there is no authenticated <c>ClaimsPrincipal</c> yet
    /// but the actor is still known — e.g. a user's own login attempt updating their
    /// lockout counters. Cleared once read, same as <see cref="PendingAuditReason"/>.
    /// </summary>
    public Guid? PendingActorOverride { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompDbContext).Assembly);
    }
}
