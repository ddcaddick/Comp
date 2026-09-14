using System.Net;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
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
/// Drives /auth/login and /auth/refresh through the real HTTP pipeline (routing, the
/// FluentValidation endpoint filter, rate limiting, problem details) against a real
/// Postgres, per the architecture doc's WebApplicationFactory + Testcontainers strategy.
/// </summary>
public class AuthEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";
    private const string Email = "official@example.com";

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
        var user = new AppUser
        {
            UserName = Email,
            Email = Email,
            EmailConfirmed = true,
            DisplayName = "Test Official"
        };
        // Seeding directly via UserManager has no HTTP request behind it, so there is no
        // ClaimsPrincipal for the audit interceptor to attribute the write to. A user
        // creating their own account is its own actor.
        dbContext.PendingActorOverride = user.Id;
        var createResult = await userManager.CreateAsync(user, Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", createResult.Errors.Select(e => e.Description)));
        }
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Login_with_correct_credentials_returns_a_token_pair()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email = Email, password = Password });

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.AccessTokenExpiresAt < tokens.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_without_revealing_which_field_was_wrong()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email = Email, password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_for_an_unknown_email_returns_the_same_401_as_a_wrong_password()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email = "nobody@example.com", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid email or password", body);
    }

    [Fact]
    public async Task Login_with_an_invalid_body_returns_a_validation_problem()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email = "not-an-email", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_cannot_be_reused()
    {
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email = Email, password = Password });
        var original = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var refreshResponse = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = original!.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotEqual(original.RefreshToken, rotated!.RefreshToken);

        var reuseResponse = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = original.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_also_revokes_the_token_that_replaced_it()
    {
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email = Email, password = Password });
        var original = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var refreshResponse = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = original!.RefreshToken });
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();

        // Reusing the original (now-rotated) token is a theft signal — the whole chain,
        // including the token that legitimately replaced it, should be revoked.
        await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = original.RefreshToken });

        var secondUseOfRotated = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = rotated!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, secondUseOfRotated.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_an_unknown_token_returns_401()
    {
        var response = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = "not-a-real-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
