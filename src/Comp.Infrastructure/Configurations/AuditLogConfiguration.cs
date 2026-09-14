using Comp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.Property(a => a.Id).UseIdentityByDefaultColumn();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Before).HasColumnType("jsonb");
        builder.Property(a => a.After).HasColumnType("jsonb");

        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAt })
            .IsDescending(false, false, true);

        // Append-only: no update or delete path is exposed for this entity anywhere in
        // the API.
    }
}
