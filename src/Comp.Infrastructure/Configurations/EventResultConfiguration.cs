using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class EventResultConfiguration : IEntityTypeConfiguration<EventResult>
{
    public void Configure(EntityTypeBuilder<EventResult> builder)
    {
        builder.ToTable("event_results");

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EventParticipant>()
            .WithMany()
            .HasForeignKey(r => r.EventParticipantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<League>()
            .WithMany()
            .HasForeignKey(r => r.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Run>()
            .WithMany()
            .HasForeignKey(r => r.BestRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.EventParticipantId).IsUnique();
        builder.HasIndex(r => new { r.EventId, r.LeagueId, r.LeaguePosition });
        builder.HasIndex(r => new { r.LeagueId, r.EventParticipantId });
    }
}
