using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class LeagueMembershipConfiguration : IEntityTypeConfiguration<LeagueMembership>
{
    public void Configure(EntityTypeBuilder<LeagueMembership> builder)
    {
        builder.ToTable("league_memberships");

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(m => m.CompetitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<League>()
            .WithMany()
            .HasForeignKey(m => m.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Shooter>()
            .WithMany()
            .HasForeignKey(m => m.ShooterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Enforces the no-mid-year-moves rule: one shooter, one league, per competition.
        builder.HasIndex(m => new { m.CompetitionId, m.ShooterId }).IsUnique();
    }
}
