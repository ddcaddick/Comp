using Comp.Application.Abstractions;
using Comp.Domain;
using Comp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Comp.Api.Tests;

/// <summary>
/// Proves the M2 acceptance criterion: every write to audited data produces an audit row
/// without the calling code doing anything, against a real Postgres.
/// </summary>
public class AuditSaveChangesInterceptorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateContext(Guid.NewGuid());
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private CompDbContext CreateContext(Guid? actorUserId)
    {
        var options = new DbContextOptionsBuilder<CompDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditSaveChangesInterceptor(new FixedCurrentUser(actorUserId)))
            .Options;

        return new CompDbContext(options);
    }

    [Fact]
    public async Task Creating_an_entity_writes_an_audit_row_with_no_extra_code()
    {
        var actorId = Guid.NewGuid();
        await using var context = CreateContext(actorId);

        var shooter = new Shooter { FirstName = "Ada", LastName = "Lovelace", CreatedByUserId = actorId };
        context.Shooters.Add(shooter);
        await context.SaveChangesAsync();

        var audit = await context.AuditLogs.SingleAsync(a => a.EntityType == nameof(Shooter));
        Assert.Equal("Created", audit.Action);
        Assert.Equal(shooter.Id, audit.EntityId);
        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Null(audit.Before);
        Assert.Contains("Ada", audit.After);
        Assert.Null(audit.Reason);
    }

    [Fact]
    public async Task Updating_an_entity_captures_only_the_changed_fields_before_and_after()
    {
        var actorId = Guid.NewGuid();
        Guid shooterId;

        await using (var context = CreateContext(actorId))
        {
            var shooter = new Shooter { FirstName = "Ada", LastName = "Lovelace", CreatedByUserId = actorId };
            context.Shooters.Add(shooter);
            await context.SaveChangesAsync();
            shooterId = shooter.Id;
        }

        await using (var context = CreateContext(actorId))
        {
            var shooter = await context.Shooters.SingleAsync(s => s.Id == shooterId);
            shooter.Nickname = "Countess";
            await context.SaveChangesAsync();

            var audit = await context.AuditLogs.SingleAsync(a => a.Action == "Updated");
            Assert.Equal(shooterId, audit.EntityId);
            Assert.Contains("Countess", audit.After!);
            Assert.DoesNotContain("FirstName", audit.Before!);
            Assert.DoesNotContain("FirstName", audit.After!);
        }
    }

    [Fact]
    public async Task Deleting_an_entity_records_the_prior_state_and_a_null_after()
    {
        var actorId = Guid.NewGuid();
        Guid shooterId;

        await using (var context = CreateContext(actorId))
        {
            var shooter = new Shooter { FirstName = "Grace", LastName = "Hopper", CreatedByUserId = actorId };
            context.Shooters.Add(shooter);
            await context.SaveChangesAsync();
            shooterId = shooter.Id;
        }

        await using (var context = CreateContext(actorId))
        {
            var shooter = await context.Shooters.SingleAsync(s => s.Id == shooterId);
            context.Shooters.Remove(shooter);
            await context.SaveChangesAsync();

            var audit = await context.AuditLogs.SingleAsync(a => a.Action == "Deleted");
            Assert.Equal(shooterId, audit.EntityId);
            Assert.Contains("Grace", audit.Before!);
            Assert.Null(audit.After);
        }
    }

    [Fact]
    public async Task An_amendment_reason_attaches_to_every_row_from_that_save_only()
    {
        var actorId = Guid.NewGuid();
        await using var context = CreateContext(actorId);

        context.Shooters.Add(new Shooter { FirstName = "Reasoned", LastName = "Write", CreatedByUserId = actorId });
        context.PendingAuditReason = "Correcting a mis-recorded time";
        await context.SaveChangesAsync();

        var firstAudit = await context.AuditLogs.SingleAsync(a => a.EntityType == nameof(Shooter));
        Assert.Equal("Correcting a mis-recorded time", firstAudit.Reason);

        context.Shooters.Add(new Shooter { FirstName = "Unreasoned", LastName = "Write", CreatedByUserId = actorId });
        await context.SaveChangesAsync();

        var secondAudit = await context.AuditLogs.SingleAsync(a => a.EntityType == nameof(Shooter) && a.Reason == null);
        Assert.Null(secondAudit.Reason);
    }

    [Fact]
    public async Task Saving_without_a_current_user_throws_rather_than_writing_an_unattributed_change()
    {
        await using var context = CreateContext(actorUserId: null);
        context.Shooters.Add(new Shooter { FirstName = "No", LastName = "Actor", CreatedByUserId = Guid.NewGuid() });

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Entry_session_heartbeats_are_not_audited()
    {
        var actorId = Guid.NewGuid();
        await using var context = CreateContext(actorId);

        var competition = new Competition
        {
            Name = "2027 Season",
            Year = 2027,
            StartsOn = new DateOnly(2027, 1, 1),
            EndsOn = new DateOnly(2027, 12, 31)
        };
        var @event = new Event
        {
            CompetitionId = competition.Id,
            EventNumber = 1,
            Name = "Week 1",
            EventDate = new DateOnly(2027, 1, 7),
            CreatedByUserId = actorId
        };
        context.Competitions.Add(competition);
        context.Events.Add(@event);
        context.EntrySessions.Add(new EntrySession { EventId = @event.Id, UserId = actorId });
        await context.SaveChangesAsync();

        var auditedTypes = await context.AuditLogs.Select(a => a.EntityType).ToListAsync();
        Assert.DoesNotContain(nameof(EntrySession), auditedTypes);
        Assert.Contains(nameof(Competition), auditedTypes);
        Assert.Contains(nameof(Event), auditedTypes);
    }

    private sealed class FixedCurrentUser(Guid? userId) : ICurrentUserAccessor
    {
        public Guid? UserId => userId;
    }
}
