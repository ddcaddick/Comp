using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Identity;

/// <summary>Seeds the four fixed roles from the security model so they exist from the first migration.</summary>
public class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    // Fixed, not Guid.CreateVersion7(): migration seed data must be deterministic across
    // every environment the migration runs in, not generated fresh each time.
    private static readonly Guid SuperAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid OfficialId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid ReadOnlyId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        // Identity's base configuration calls ToTable("AspNetRoles") itself; an explicit
        // table name isn't re-cased by the snake_case naming convention, so it is
        // overridden here to match the rest of the schema.
        builder.ToTable("asp_net_roles");

        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = SuperAdminId,
                Name = Roles.SuperAdmin,
                NormalizedName = Roles.SuperAdmin,
                ConcurrencyStamp = SuperAdminId.ToString()
            },
            new IdentityRole<Guid>
            {
                Id = AdminId,
                Name = Roles.Admin,
                NormalizedName = Roles.Admin,
                ConcurrencyStamp = AdminId.ToString()
            },
            new IdentityRole<Guid>
            {
                Id = OfficialId,
                Name = Roles.Official,
                NormalizedName = Roles.Official,
                ConcurrencyStamp = OfficialId.ToString()
            },
            new IdentityRole<Guid>
            {
                Id = ReadOnlyId,
                Name = Roles.ReadOnly,
                NormalizedName = Roles.ReadOnly,
                ConcurrencyStamp = ReadOnlyId.ToString()
            });
    }
}
