using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Competitions;
using Comp.Contracts.Events;
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
/// Drives /competitions through the real HTTP pipeline, including that managing
/// competitions is Super Admin only per the architecture doc's security model, while
/// viewing them is open to any authenticated role.
/// </summary>
public class CompetitionEndpointTests : IAsyncLifetime
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
            var email = EmailFor(role);
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = role };
            dbContext.PendingActorOverride = user.Id;
            var createResult = await userManager.CreateAsync(user, Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join(", ", createResult.Errors.Select(e => e.Description)));
            }

            dbContext.PendingActorOverride = user.Id;
            await userManager.AddToRoleAsync(user, role);
        }
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Creating_a_competition_is_allowed_for_super_admin_only()
    {
        using var superAdminClient = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var response = await superAdminClient.PostAsJsonAsync("/competitions",
            new { name = "2027 Season", year = 2027, startsOn = "2027-01-01", endsOn = "2027-12-31" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var adminClient = await AuthenticatedClientAsync(Roles.Admin);
        var forbidden = await adminClient.PostAsJsonAsync("/competitions",
            new { name = "Another Season", year = 2028, startsOn = "2028-01-01", endsOn = "2028-12-31" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Creating_a_duplicate_year_and_name_returns_a_conflict()
    {
        using var client = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var body = new { name = "2027 Season", year = 2027, startsOn = "2027-01-01", endsOn = "2027-12-31" };

        var first = await client.PostAsJsonAsync("/competitions", body);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/competitions", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Creating_a_competition_with_an_invalid_body_returns_a_validation_problem()
    {
        using var client = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var response = await client.PostAsJsonAsync("/competitions",
            new { name = "", year = 1899, startsOn = "2027-12-31", endsOn = "2027-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listing_competitions_is_readable_by_any_authenticated_role()
    {
        using var superAdminClient = await AuthenticatedClientAsync(Roles.SuperAdmin);
        await superAdminClient.PostAsJsonAsync("/competitions",
            new { name = "Listable Season", year = 2029, startsOn = "2029-01-01", endsOn = "2029-12-31" });

        using var readOnlyClient = await AuthenticatedClientAsync(Roles.ReadOnly);
        var competitions = await readOnlyClient.GetFromJsonAsync<List<CompetitionResponse>>("/competitions");

        Assert.Contains(competitions!, c => c.Name == "Listable Season" && c.Year == 2029 && c.Status == "Planning");
    }

    [Fact]
    public async Task Closing_a_competition_is_one_way_and_super_admin_only()
    {
        using var superAdminClient = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var createResponse = await superAdminClient.PostAsJsonAsync("/competitions",
            new { name = "Closeable Season", year = 2030, startsOn = "2030-01-01", endsOn = "2030-12-31" });
        var competition = await createResponse.Content.ReadFromJsonAsync<CompetitionResponse>();

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var forbidden = await officialClient.PostAsync($"/competitions/{competition!.Id}/close", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var close = await superAdminClient.PostAsync($"/competitions/{competition.Id}/close", content: null);
        close.EnsureSuccessStatusCode();
        var closed = await close.Content.ReadFromJsonAsync<CompetitionResponse>();
        Assert.Equal("Closed", closed!.Status);

        var closeAgain = await superAdminClient.PostAsync($"/competitions/{competition.Id}/close", content: null);
        Assert.Equal(HttpStatusCode.Conflict, closeAgain.StatusCode);
    }

    [Fact]
    public async Task Closing_an_unknown_competition_returns_404()
    {
        using var client = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var response = await client.PostAsync($"/competitions/{Guid.NewGuid()}/close", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listing_competitions_reports_total_shooters_average_and_events_remaining()
    {
        using var superAdmin = await AuthenticatedClientAsync(Roles.SuperAdmin);
        using var admin = await AuthenticatedClientAsync(Roles.Admin);
        using var official = await AuthenticatedClientAsync(Roles.Official);

        var competitionResponse = await superAdmin.PostAsJsonAsync("/competitions",
            new { name = "Stats Season", year = 2031, startsOn = "2031-01-01", endsOn = "2031-12-31" });
        var competitionId = (await competitionResponse.Content.ReadFromJsonAsync<CompetitionResponse>())!.Id;

        var shooterA = await CreateShooterAsync(admin, "Alice", "Shot-Both");
        var shooterB = await CreateShooterAsync(admin, "Bob", "Dnf-Once");
        var shooterC = await CreateShooterAsync(admin, "Carol", "Never-Called-Up");

        var event1 = await CreateEventAsync(admin, competitionId, 1);
        var event2 = await CreateEventAsync(admin, competitionId, 2);

        // Event 1: A gets a valid time, B is DNF (still "shot"), C is added but never
        // recorded a run at all -- so two distinct shooters actually shot this event.
        var participantA1 = await AddParticipantAsync(official, event1, shooterA.Id);
        var participantB1 = await AddParticipantAsync(official, event1, shooterB.Id);
        await AddParticipantAsync(official, event1, shooterC.Id);
        await SaveRunAsync(official, event1, participantA1, 1, rawTimeMs: 90_000, isDnf: false);
        await SaveRunAsync(official, event1, participantB1, 1, rawTimeMs: null, isDnf: true);

        // Event 2: only A shoots again -- same shooter as event 1, so Total shooters stays
        // 2 (A, B) rather than double-counting A, but the per-event average still treats
        // event 2 as "1 shooter shot" for its own count.
        var participantA2 = await AddParticipantAsync(official, event2, shooterA.Id);
        await SaveRunAsync(official, event2, participantA2, 1, rawTimeMs: 100_000, isDnf: false);

        foreach (var to in new[] { "Setup", "InProgress", "Review", "Finalised" })
        {
            (await admin.PostAsJsonAsync($"/events/{event1}/transition", new { to })).EnsureSuccessStatusCode();
        }
        // event2 is deliberately left un-finalised, so EventsRemaining should be 1 (of 2).

        var competitions = await official.GetFromJsonAsync<List<CompetitionResponse>>("/competitions");
        var stats = competitions!.Single(c => c.Id == competitionId);

        Assert.Equal(2, stats.TotalShooters);
        Assert.Equal(1.5m, stats.AverageShootersPerEvent);
        Assert.Equal(1, stats.EventsRemaining);
    }

    private static string EmailFor(string role) => $"{role.ToLowerInvariant()}@example.com";

    private static async Task<ShooterResponse> CreateShooterAsync(HttpClient client, string firstName, string lastName)
    {
        var response = await client.PostAsJsonAsync("/shooters", new { firstName, lastName });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShooterResponse>())!;
    }

    private static async Task<Guid> CreateEventAsync(HttpClient client, Guid competitionId, int eventNumber)
    {
        var response = await client.PostAsJsonAsync("/events",
            new { competitionId, eventNumber, name = $"Event {eventNumber}", eventDate = "2031-01-07" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!.Id;
    }

    private static async Task<Guid> AddParticipantAsync(HttpClient client, Guid eventId, Guid shooterId)
    {
        var response = await client.PostAsJsonAsync($"/events/{eventId}/participants", new { shooterId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventParticipantResponse>())!.Id;
    }

    private static async Task SaveRunAsync(
        HttpClient client, Guid eventId, Guid participantId, int runNumber, int? rawTimeMs, bool isDnf)
    {
        var response = await client.PutAsJsonAsync(
            $"/events/{eventId}/participants/{participantId}/runs/{runNumber}",
            new { rawTimeMs, penaltyCount = 0, isDnf });
        response.EnsureSuccessStatusCode();
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
}
