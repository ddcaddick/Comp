using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.ToTable("competitions");

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => new { c.Year, c.Name }).IsUnique();
    }
}
