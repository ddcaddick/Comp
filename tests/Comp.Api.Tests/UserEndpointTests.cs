using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Users;
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
/// Drives /users through the real HTTP pipeline: this is Super Admin-only per the
/// security model (stricter than every other admin-CRUD, which is usually Super Admin +
/// Admin too), covers create/edit/deactivate/reactivate, an admin-set password reset,
/// unlocking a real Identity lockout, and granting/revoking CanAmendPublished.
/// </summary>
public class UserEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _superAdmin = null!;

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
            var email = EmailFor(role);
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = role };
            dbContext.PendingActorOverride = user.Id;
            await userManager.CreateAsync(user, Password);
            dbContext.PendingActorOverride = user.Id;
            await userManager.AddToRoleAsync(user, role);
        }

        _superAdmin = await AuthenticatedClientAsync(Roles.SuperAdmin);
    }

    public async Task DisposeAsync()
    {
        _superAdmin.Dispose();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Managing_users_is_allowed_for_super_admin_but_not_admin()
    {
        using var admin = await AuthenticatedClientAsync(Roles.Admin);
        var forbidden = await admin.GetAsync("/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var response = await _superAdmin.GetAsync("/users");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Creating_a_user_assigns_the_requested_role_and_lets_them_sign_in()
    {
        var created = await CreateUserAsync("new.official@example.com", "New Official", Roles.Official);
        Assert.Equal(Roles.Official, created.Role);
        Assert.True(created.IsActive);
        Assert.False(created.CanAmendPublished);

        var login = await _client.PostAsJsonAsync("/auth/login",
            new { email = "new.official@example.com", password = Password });
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Creating_a_user_with_a_duplicate_email_returns_a_conflict()
    {
        await CreateUserAsync("dupe@example.com", "First", Roles.Official);

        var response = await _superAdmin.PostAsJsonAsync("/users",
            new { email = "dupe@example.com", displayName = "Second", role = Roles.Official, password = Password });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_with_an_unknown_role_returns_a_conflict()
    {
        var response = await _superAdmin.PostAsJsonAsync("/users",
            new { email = "badrole@example.com", displayName = "Bad Role", role = "NOT_A_ROLE", password = Password });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Updating_display_name_and_role_both_apply()
    {
        var created = await CreateUserAsync("editable@example.com", "Before Name", Roles.Official);

        var response = await _superAdmin.PatchAsJsonAsync($"/users/{created.Id}",
            new { displayName = "After Name", role = Roles.Admin });
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("After Name", updated!.DisplayName);
        Assert.Equal(Roles.Admin, updated.Role);
    }

    [Fact]
    public async Task A_super_admin_cannot_change_their_own_role()
    {
        var me = await GetUserByEmailAsync(EmailFor(Roles.SuperAdmin));

        var response = await _superAdmin.PatchAsJsonAsync($"/users/{me.Id}",
            new { displayName = me.DisplayName, role = Roles.Admin });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_super_admin_can_rename_themselves_without_touching_their_role()
    {
        var me = await GetUserByEmailAsync(EmailFor(Roles.SuperAdmin));

        var response = await _superAdmin.PatchAsJsonAsync($"/users/{me.Id}",
            new { displayName = "Renamed Self", role = me.Role });
        response.EnsureSuccessStatusCode();
        Assert.Equal("Renamed Self", (await response.Content.ReadFromJsonAsync<UserResponse>())!.DisplayName);
    }

    [Fact]
    public async Task Deactivating_a_user_prevents_sign_in_and_reactivating_restores_it()
    {
        var created = await CreateUserAsync("togglable@example.com", "Togglable", Roles.Official);

        var deactivate = await _superAdmin.PostAsync($"/users/{created.Id}/deactivate", content: null);
        deactivate.EnsureSuccessStatusCode();
        Assert.False((await deactivate.Content.ReadFromJsonAsync<UserResponse>())!.IsActive);

        var blockedLogin = await _client.PostAsJsonAsync("/auth/login",
            new { email = "togglable@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, blockedLogin.StatusCode);

        // Idempotent: deactivating an already-deactivated user is still a success.
        var again = await _superAdmin.PostAsync($"/users/{created.Id}/deactivate", content: null);
        again.EnsureSuccessStatusCode();

        var reactivate = await _superAdmin.PostAsync($"/users/{created.Id}/reactivate", content: null);
        reactivate.EnsureSuccessStatusCode();
        Assert.True((await reactivate.Content.ReadFromJsonAsync<UserResponse>())!.IsActive);

        var restoredLogin = await _client.PostAsJsonAsync("/auth/login",
            new { email = "togglable@example.com", password = Password });
        restoredLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_super_admin_cannot_deactivate_their_own_account()
    {
        var me = await GetUserByEmailAsync(EmailFor(Roles.SuperAdmin));

        var response = await _superAdmin.PostAsync($"/users/{me.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resetting_a_password_lets_the_user_sign_in_with_the_new_one()
    {
        var created = await CreateUserAsync("resettable@example.com", "Resettable", Roles.Official);

        var reset = await _superAdmin.PostAsJsonAsync($"/users/{created.Id}/reset-password",
            new { password = "A Brand New Password 1!" });
        reset.EnsureSuccessStatusCode();

        var oldPasswordLogin = await _client.PostAsJsonAsync("/auth/login",
            new { email = "resettable@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await _client.PostAsJsonAsync("/auth/login",
            new { email = "resettable@example.com", password = "A Brand New Password 1!" });
        newPasswordLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Unlocking_a_locked_out_account_lets_it_sign_in_again()
    {
        var created = await CreateUserAsync("lockout@example.com", "Locked Out", Roles.Official);

        // Simulated directly against Identity (ASP.NET Core Identity's default lockout is 5
        // failed attempts) rather than via repeated /auth/login calls, which would collide
        // with that endpoint's own 5-per-minute-per-IP rate limit -- the setup login above
        // already spends one of those five permits, so looping over HTTP here would trip
        // the limiter (a 429) before Identity's separate attempt counter ever reached 5.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var dbContext = scope.ServiceProvider.GetRequiredService<CompDbContext>();
            var user = (await userManager.FindByIdAsync(created.Id.ToString()))!;
            for (var i = 0; i < 5; i++)
            {
                // No HTTP request is driving this, so there's no ClaimsPrincipal for the
                // audit interceptor to attribute the change to -- same override AuthService
                // itself sets before calling AccessFailedAsync on a failed login.
                dbContext.PendingActorOverride = user.Id;
                await userManager.AccessFailedAsync(user);
            }
        }

        var lockedList = await _superAdmin.GetFromJsonAsync<List<UserResponse>>("/users");
        Assert.True(lockedList!.Single(u => u.Id == created.Id).IsLockedOut);

        var lockedLogin = await _client.PostAsJsonAsync("/auth/login",
            new { email = "lockout@example.com", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedLogin.StatusCode);

        var unlock = await _superAdmin.PostAsync($"/users/{created.Id}/unlock", content: null);
        unlock.EnsureSuccessStatusCode();
        Assert.False((await unlock.Content.ReadFromJsonAsync<UserResponse>())!.IsLockedOut);

        var restoredLogin = await _client.PostAsJsonAsync("/auth/login",
            new { email = "lockout@example.com", password = Password });
        restoredLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Granting_and_revoking_amend_published_both_require_a_reason()
    {
        var created = await CreateUserAsync("amendable@example.com", "Amendable", Roles.Admin);

        var missingReason = await _superAdmin.PostAsJsonAsync($"/users/{created.Id}/amend-published",
            new { grant = true, reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var grant = await _superAdmin.PostAsJsonAsync($"/users/{created.Id}/amend-published",
            new { grant = true, reason = "Needed to correct last week's Division A result." });
        grant.EnsureSuccessStatusCode();
        Assert.True((await grant.Content.ReadFromJsonAsync<UserResponse>())!.CanAmendPublished);

        var revoke = await _superAdmin.PostAsJsonAsync($"/users/{created.Id}/amend-published",
            new { grant = false, reason = "No longer needed." });
        revoke.EnsureSuccessStatusCode();
        Assert.False((await revoke.Content.ReadFromJsonAsync<UserResponse>())!.CanAmendPublished);
    }

    [Fact]
    public async Task Actions_on_an_unknown_user_return_404()
    {
        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await _superAdmin.PatchAsJsonAsync($"/users/{unknown}", new { displayName = "X", role = Roles.Official })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _superAdmin.PostAsync($"/users/{unknown}/deactivate", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _superAdmin.PostAsync($"/users/{unknown}/reactivate", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _superAdmin.PostAsync($"/users/{unknown}/unlock", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _superAdmin.PostAsJsonAsync($"/users/{unknown}/reset-password", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _superAdmin.PostAsJsonAsync($"/users/{unknown}/amend-published", new { grant = true, reason = "why" })).StatusCode);
    }

    private async Task<UserResponse> CreateUserAsync(string email, string displayName, string role)
    {
        var response = await _superAdmin.PostAsJsonAsync("/users", new { email, displayName, role, password = Password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    private async Task<UserResponse> GetUserByEmailAsync(string email)
    {
        var list = await _superAdmin.GetFromJsonAsync<List<UserResponse>>("/users");
        return list!.Single(u => u.Email == email);
    }

    private static string EmailFor(string role) => $"{role.ToLowerInvariant()}@example.com";

    private async Task<HttpClient> AuthenticatedClientAsync(string role)
    {
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email = EmailFor(role), password = Password });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }
}
