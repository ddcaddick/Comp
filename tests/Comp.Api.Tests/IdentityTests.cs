using Comp.Infrastructure;
using Comp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Comp.Api.Tests;

/// <summary>
/// Proves the M2 identity setup: the four roles exist from the first migration, a user
/// can be created and assigned one, and CanAmendPublished only ever reaches the sign-in
/// claims for a user it was actually granted to.
/// </summary>
public class IdentityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<CompDbContext>(options =>
            options.UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention());
        services.AddLogging();
        // WebApplication.CreateBuilder registers this by default; a bare ServiceCollection
        // needs it explicitly for AddDefaultTokenProviders()'s DataProtectorTokenProvider.
        services.AddDataProtection();
        services
            .AddIdentityCore<AppUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CompDbContext>()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CompDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task The_four_roles_exist_from_the_migration_alone()
    {
        await using var scope = _services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in Roles.All)
        {
            Assert.True(await roleManager.RoleExistsAsync(role), $"Expected role '{role}' to be seeded.");
        }
    }

    [Fact]
    public async Task Creating_a_user_and_assigning_a_role_round_trips()
    {
        await using var scope = _services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser { UserName = "official@example.com", Email = "official@example.com", DisplayName = "An Official" };
        var createResult = await userManager.CreateAsync(user, "Correct horse battery staple 9!");
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(e => e.Description)));

        var roleResult = await userManager.AddToRoleAsync(user, Roles.Official);
        Assert.True(roleResult.Succeeded);

        Assert.True(await userManager.IsInRoleAsync(user, Roles.Official));
    }

    [Fact]
    public async Task CanAmendPublished_reaches_the_sign_in_claims_only_when_granted()
    {
        await using var scope = _services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();

        var grantedUser = new AppUser
        {
            UserName = "admin@example.com",
            Email = "admin@example.com",
            DisplayName = "Granted Admin",
            CanAmendPublished = true
        };
        var ungrantedUser = new AppUser
        {
            UserName = "official2@example.com",
            Email = "official2@example.com",
            DisplayName = "Ordinary Official",
            CanAmendPublished = false
        };
        var grantedCreate = await userManager.CreateAsync(grantedUser, "Correct horse battery staple 9!");
        var ungrantedCreate = await userManager.CreateAsync(ungrantedUser, "Correct horse battery staple 9!");
        Assert.True(grantedCreate.Succeeded, string.Join(", ", grantedCreate.Errors.Select(e => e.Description)));
        Assert.True(ungrantedCreate.Succeeded, string.Join(", ", ungrantedCreate.Errors.Select(e => e.Description)));

        var grantedPrincipal = await claimsFactory.CreateAsync(grantedUser);
        var ungrantedPrincipal = await claimsFactory.CreateAsync(ungrantedUser);

        Assert.True(grantedPrincipal.HasClaim(AppUserClaimsPrincipalFactory.AmendPublishedClaimType, "true"));
        Assert.False(ungrantedPrincipal.HasClaim(AppUserClaimsPrincipalFactory.AmendPublishedClaimType, "true"));
    }
}
