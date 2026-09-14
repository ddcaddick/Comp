using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("runs", t =>
        {
            t.HasCheckConstraint("ck_runs_raw_time_ms_positive",
                "raw_time_ms is null or raw_time_ms > 0");
            t.HasCheckConstraint("ck_runs_penalty_count_non_negative",
                "penalty_count >= 0");
            // Makes "DNF with a time" unrepresentable, not merely discouraged.
            t.HasCheckConstraint("ck_runs_dnf_has_no_time",
                "(is_dnf and raw_time_ms is null) or (not is_dnf and raw_time_ms is not null)");
        });

        builder.Property(r => r.IdempotencyKey).HasMaxLength(200);

        builder.HasOne<EventParticipant>()
            .WithMany()
            .HasForeignKey(r => r.EventParticipantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.EventParticipantId, r.RunNumber }).IsUnique();

        // Postgres treats multiple NULLs as distinct, so runs with no key (e.g. entered
        // from the web) don't collide with each other.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();
    }
}
