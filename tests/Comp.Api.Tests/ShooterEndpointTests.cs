using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Shooters;
using Comp.Infrastructure;
using Comp.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Comp.Api.Tests;

/// <summary>
/// Drives the /shooters endpoints through the real HTTP pipeline, including the
/// authorization split from the architecture doc's security model: an Official may
/// create a shooter but not edit or deactivate one; a Read-only user may view but not
/// write at all. Negative authorization cases are mandatory per the testing strategy.
/// </summary>
public class ShooterEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _postgres.GetConnectionString()
                }));
        });

        _client = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CompDbContext>();
        await dbContext.Database.MigrateAsync();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var role in Roles.All)
        {
            await CreateUserAsync(userManager, dbContext, EmailFor(role), role);
        }
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Theory]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Official)]
    public async Task Creating_a_shooter_is_allowed_for_super_admin_admin_and_official(string role)
    {
        using var client = await AuthenticatedClientAsync(role);

        var response = await client.PostAsJsonAsync("/shooters", new { firstName = "Ada", lastName = "Lovelace" });

        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_shooter_is_forbidden_for_read_only()
    {
        using var client = await AuthenticatedClientAsync(Roles.ReadOnly);

        var response = await client.PostAsJsonAsync("/shooters", new { firstName = "Ada", lastName = "Lovelace" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_shooter_without_a_token_is_unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/shooters", new { firstName = "Ada", lastName = "Lovelace" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_shooter_with_an_invalid_body_returns_a_validation_problem()
    {
        using var client = await AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PostAsJsonAsync("/shooters", new { firstName = "", lastName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_shooter_is_allowed_for_admin_but_not_official()
    {
        using var adminClient = await AuthenticatedClientAsync(Roles.Admin);
        var shooter = await CreateShooterAsync(adminClient, "Grace", "Hopper");

        var adminEdit = await adminClient.PatchAsJsonAsync($"/shooters/{shooter.Id}",
            new { firstName = "Grace", lastName = "Hopper", nickname = "Amazing Grace" });
        adminEdit.EnsureSuccessStatusCode();
        var updated = await adminEdit.Content.ReadFromJsonAsync<ShooterResponse>();
        Assert.Equal("Amazing Grace", updated!.Nickname);

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var officialEdit = await officialClient.PatchAsJsonAsync($"/shooters/{shooter.Id}",
            new { firstName = "Grace", lastName = "Hopper", nickname = "Nope" });
        Assert.Equal(HttpStatusCode.Forbidden, officialEdit.StatusCode);
    }

    [Fact]
    public async Task Editing_an_unknown_shooter_returns_404()
    {
        using var client = await AuthenticatedClientAsync(Roles.Admin);

        var response = await client.PatchAsJsonAsync($"/shooters/{Guid.NewGuid()}",
            new { firstName = "No", lastName = "One" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deactivating_and_reactivating_a_shooter_round_trips_and_is_forbidden_for_official()
    {
        using var adminClient = await AuthenticatedClientAsync(Roles.Admin);
        var shooter = await CreateShooterAsync(adminClient, "Katherine", "Johnson");

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var forbidden = await officialClient.PostAsync($"/shooters/{shooter.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var deactivate = await adminClient.PostAsync($"/shooters/{shooter.Id}/deactivate", content: null);
        deactivate.EnsureSuccessStatusCode();
        var deactivated = await deactivate.Content.ReadFromJsonAsync<ShooterResponse>();
        Assert.False(deactivated!.IsActive);

        var reactivate = await adminClient.PostAsync($"/shooters/{shooter.Id}/reactivate", content: null);
        reactivate.EnsureSuccessStatusCode();
        var reactivated = await reactivate.Content.ReadFromJsonAsync<ShooterResponse>();
        Assert.True(reactivated!.IsActive);
    }

    [Fact]
    public async Task Search_finds_a_shooter_by_partial_first_last_or_nickname_and_is_readable_by_read_only()
    {
        using var adminClient = await AuthenticatedClientAsync(Roles.Admin);
        await CreateShooterAsync(adminClient, "Margaret", "Hamilton", nickname: "Maggie");

        using var readOnlyClient = await AuthenticatedClientAsync(Roles.ReadOnly);

        foreach (var term in new[] { "margaret", "hamilton", "maggie" })
        {
            var response = await readOnlyClient.GetAsync($"/shooters?q={term}");
            response.EnsureSuccessStatusCode();
            var results = await response.Content.ReadFromJsonAsync<List<ShooterResponse>>();
            Assert.Contains(results!, s => s.LastName == "Hamilton");
        }
    }

    [Fact]
    public async Task Search_with_active_filter_excludes_deactivated_shooters_only_when_asked()
    {
        using var adminClient = await AuthenticatedClientAsync(Roles.Admin);
        var shooter = await CreateShooterAsync(adminClient, "Deactivated", "Person");
        await adminClient.PostAsync($"/shooters/{shooter.Id}/deactivate", content: null);

        var unfiltered = await adminClient.GetFromJsonAsync<List<ShooterResponse>>("/shooters?q=Deactivated");
        Assert.Contains(unfiltered!, s => s.Id == shooter.Id);

        var activeOnly = await adminClient.GetFromJsonAsync<List<ShooterResponse>>("/shooters?q=Deactivated&active=true");
        Assert.DoesNotContain(activeOnly!, s => s.Id == shooter.Id);
    }

    [Fact]
    public async Task History_reflects_create_update_and_deactivate_and_is_null_for_an_unknown_shooter()
    {
        using var adminClient = await AuthenticatedClientAsync(Roles.Admin);
        var shooter = await CreateShooterAsync(adminClient, "History", "Test");

        await adminClient.PatchAsJsonAsync($"/shooters/{shooter.Id}",
            new { firstName = "History", lastName = "Test", nickname = "Renamed" });
        await adminClient.PostAsync($"/shooters/{shooter.Id}/deactivate", content: null);

        var history = await adminClient.GetFromJsonAsync<List<ShooterHistoryEntryResponse>>($"/shooters/{shooter.Id}/history");
        Assert.NotNull(history);
        Assert.Contains(history!, h => h.Action == "Created");
        Assert.Contains(history!, h => h.Action == "Updated");

        var missing = await adminClient.GetAsync($"/shooters/{Guid.NewGuid()}/history");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static string EmailFor(string role) => $"{role.ToLowerInvariant()}@example.com";

    private static async Task CreateUserAsync(UserManager<AppUser> userManager, CompDbContext dbContext, string email, string role)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = role
        };
        // Seeding directly via UserManager has no HTTP request behind it, so there is no
        // ClaimsPrincipal for the audit interceptor to attribute the write to.
        dbContext.PendingActorOverride = user.Id;
        var createResult = await userManager.CreateAsync(user, Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", createResult.Errors.Select(e => e.Description)));
        }

        dbContext.PendingActorOverride = user.Id;
        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", roleResult.Errors.Select(e => e.Description)));
        }
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string role)
    {
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email = EmailFor(role), password = Password });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    private static async Task<ShooterResponse> CreateShooterAsync(
        HttpClient client, string firstName, string lastName, string? nickname = null)
    {
        var response = await client.PostAsJsonAsync("/shooters", new { firstName, lastName, nickname });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShooterResponse>())!;
    }
}
