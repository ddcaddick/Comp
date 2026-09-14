using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Identity;

// Identity's base configuration calls ToTable("AspNetUserRoles") etc. on each of these
// itself, and an explicit table name isn't re-cased by the snake_case naming convention
// (only convention-derived names are). These exist purely to rename the tables to match
// the rest of the schema — every other aspect of their configuration is Identity's own.

public class IdentityUserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) =>
        builder.ToTable("asp_net_user_roles");
}

public class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) =>
        builder.ToTable("asp_net_user_claims");
}

public class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) =>
        builder.ToTable("asp_net_user_logins");
}

public class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) =>
        builder.ToTable("asp_net_user_tokens");
}

public class IdentityRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) =>
        builder.ToTable("asp_net_role_claims");
}
