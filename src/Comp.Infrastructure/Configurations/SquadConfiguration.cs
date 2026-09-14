using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class SquadConfiguration : IEntityTypeConfiguration<Squad>
{
    public void Configure(EntityTypeBuilder<Squad> builder)
    {
        builder.ToTable("squads");

        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(s => s.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.EventId, s.SquadNumber }).IsUnique();
    }
}
