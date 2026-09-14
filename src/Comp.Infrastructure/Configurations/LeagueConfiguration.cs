using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    public void Configure(EntityTypeBuilder<League> builder)
    {
        builder.ToTable("leagues");

        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(l => l.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.CompetitionId, l.Tier }).IsUnique();
        builder.HasIndex(l => new { l.CompetitionId, l.Name }).IsUnique();
    }
}
