using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class ShooterConfiguration : IEntityTypeConfiguration<Shooter>
{
    public void Configure(EntityTypeBuilder<Shooter> builder)
    {
        builder.ToTable("shooters");

        builder.Property(s => s.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.LastName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Nickname).HasMaxLength(100);
        builder.Property(s => s.MembershipNo).HasMaxLength(50);

        // The trigram search index on
        // (first_name || ' ' || last_name || ' ' || coalesce(nickname, ''))
        // is an expression index EF Core cannot represent; it is added by hand in the
        // migration (see docs/m2-wiring.md).

        // Deliberately no HasQueryFilter(s => s.IsActive): a global filter would quietly
        // remove deactivated shooters from historical results. Only the participant
        // selection query filters, and it does so explicitly.
    }
}
