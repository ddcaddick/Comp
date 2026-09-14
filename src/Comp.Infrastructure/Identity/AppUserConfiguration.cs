using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comp.Infrastructure.Identity;

/// <summary>
/// Identity's own <c>IdentityUserContext</c> configuration already sets up the keys and
/// indexes on the built-in columns. It also calls <c>ToTable("AspNetUsers")</c> itself,
/// and an explicit table name isn't re-cased by the snake_case naming convention (only
/// convention-derived names are), so it has to be overridden here to match the rest of
/// the schema.
/// </summary>
public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("asp_net_users");
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
    }
}
