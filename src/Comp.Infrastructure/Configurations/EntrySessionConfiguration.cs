using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class EntrySessionConfiguration : IEntityTypeConfiguration<EntrySession>
{
    public void Configure(EntityTypeBuilder<EntrySession> builder)
    {
        builder.ToTable("entry_sessions");

        builder.HasKey(s => s.EventId);

        builder.HasOne<Event>()
            .WithOne()
            .HasForeignKey<EntrySession>(s => s.EventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
