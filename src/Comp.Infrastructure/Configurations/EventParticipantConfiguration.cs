using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class EventParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> builder)
    {
        builder.ToTable("event_participants");

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(p => p.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Shooter>()
            .WithMany()
            .HasForeignKey(p => p.ShooterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<League>()
            .WithMany()
            .HasForeignKey(p => p.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Squad>()
            .WithMany()
            .HasForeignKey(p => p.SquadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.EventId, p.ShooterId }).IsUnique();

        // Deliberately no unique index on (SquadId, PositionInSquad): reordering a squad
        // swaps two positions, which a non-deferrable unique index rejects mid-transaction.
        // A duplicated position is a display-order glitch, not a corrupted result, so it
        // does not justify a deferrable constraint.
    }
}
