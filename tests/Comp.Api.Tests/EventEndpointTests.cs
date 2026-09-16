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
/// Drives /events through the real HTTP pipeline: creating and editing events is Super
/// Admin/Admin only per the security model; the transition state machine only allows one
/// step at a time along Draft → Setup → InProgress → Review, and refuses Finalised
/// entirely (not available until M8).
/// </summary>
public class EventEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _admin = null!;
    private Guid _competitionId;

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

        _admin = await AuthenticatedClientAsync(Roles.Admin);

        // Managing competitions is Super Admin only — Admin can create/edit events, but
        // not the competition they belong to.
        using var superAdmin = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var competitionResponse = await superAdmin.PostAsJsonAsync("/competitions",
            new { name = "M6 Season", year = 2040, startsOn = "2040-01-01", endsOn = "2040-12-31" });
        competitionResponse.EnsureSuccessStatusCode();
        _competitionId = (await competitionResponse.Content.ReadFromJsonAsync<CompetitionResponse>())!.Id;
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Creating_an_event_is_allowed_for_admin_but_not_official()
    {
        var response = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 1, name = "Week 1", eventDate = "2040-01-07" });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Draft", created!.Status);
        Assert.Equal(5.00m, created.PenaltySeconds); // default
        Assert.Equal(2, created.RunsPerShooter); // default

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var forbidden = await officialClient.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 2, name = "Week 2", eventDate = "2040-01-14" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Creating_an_event_under_an_unknown_competition_returns_404()
    {
        var response = await _admin.PostAsJsonAsync("/events",
            new { competitionId = Guid.NewGuid(), eventNumber = 1, name = "Week 1", eventDate = "2040-01-07" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_duplicate_event_number_in_the_same_competition_returns_a_conflict()
    {
        var body = new { competitionId = _competitionId, eventNumber = 5, name = "Week 5", eventDate = "2040-02-01" };
        var first = await _admin.PostAsJsonAsync("/events", body);
        first.EnsureSuccessStatusCode();

        var second = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 5, name = "Different Name", eventDate = "2040-02-08" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Creating_an_event_with_an_invalid_body_returns_a_validation_problem()
    {
        var response = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 0, name = "", eventDate = "2040-01-07" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listing_events_can_be_filtered_by_competition_and_status()
    {
        var createResponse = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 10, name = "Filterable", eventDate = "2040-03-01" });
        createResponse.EnsureSuccessStatusCode();

        var byCompetition = await _admin.GetFromJsonAsync<List<EventResponse>>($"/events?competitionId={_competitionId}");
        Assert.Contains(byCompetition!, e => e.Name == "Filterable");

        var byStatus = await _admin.GetFromJsonAsync<List<EventResponse>>(
            $"/events?competitionId={_competitionId}&status=Draft");
        Assert.Contains(byStatus!, e => e.Name == "Filterable");

        var wrongStatus = await _admin.GetFromJsonAsync<List<EventResponse>>(
            $"/events?competitionId={_competitionId}&status=InProgress");
        Assert.DoesNotContain(wrongStatus!, e => e.Name == "Filterable");
    }

    [Fact]
    public async Task Updating_an_event_is_allowed_for_admin_but_not_official()
    {
        var created = await CreateEventAsync(20, "Editable");

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var forbidden = await officialClient.PatchAsJsonAsync($"/events/{created.Id}",
            new { name = "Nope", eventDate = "2040-04-01", penaltySeconds = 5.00m, runsPerShooter = 2, countsForStandings = true });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var response = await _admin.PatchAsJsonAsync($"/events/{created.Id}",
            new { name = "Renamed", eventDate = "2040-04-02", penaltySeconds = 3.00m, runsPerShooter = 3, countsForStandings = false });
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<EventResponse>();
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal(3.00m, updated.PenaltySeconds);
        Assert.Equal(3, updated.RunsPerShooter);
        Assert.False(updated.CountsForStandings);
    }

    [Fact]
    public async Task Updating_an_unknown_event_returns_404()
    {
        var response = await _admin.PatchAsJsonAsync($"/events/{Guid.NewGuid()}",
            new { name = "No", eventDate = "2040-01-01", penaltySeconds = 5.00m, runsPerShooter = 2, countsForStandings = true });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transition_only_allows_one_step_at_a_time_and_never_reaches_finalised()
    {
        var created = await CreateEventAsync(30, "Transitions");

        // Draft -> InProgress skips a step.
        var skip = await _admin.PostAsJsonAsync($"/events/{created.Id}/transition", new { to = "InProgress" });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);

        // Draft -> Setup is one step.
        var toSetup = await _admin.PostAsJsonAsync($"/events/{created.Id}/transition", new { to = "Setup" });
        toSetup.EnsureSuccessStatusCode();
        Assert.Equal("Setup", (await toSetup.Content.ReadFromJsonAsync<EventResponse>())!.Status);

        // Setup -> Draft (one step back) is allowed.
        var back = await _admin.PostAsJsonAsync($"/events/{created.Id}/transition", new { to = "Draft" });
        back.EnsureSuccessStatusCode();
        Assert.Equal("Draft", (await back.Content.ReadFromJsonAsync<EventResponse>())!.Status);

        // Finalising isn't available yet, from any status.
        var finalise = await _admin.PostAsJsonAsync($"/events/{created.Id}/transition", new { to = "Finalised" });
        Assert.Equal(HttpStatusCode.Conflict, finalise.StatusCode);
    }

    [Fact]
    public async Task Transition_is_forbidden_for_official()
    {
        var created = await CreateEventAsync(31, "Forbidden Transition");

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var response = await officialClient.PostAsJsonAsync($"/events/{created.Id}/transition", new { to = "Setup" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Transition_with_an_unrecognised_status_returns_a_validation_problem()
    {
        var created = await CreateEventAsync(32, "Bad Status");

        var response = await _admin.PostAsJsonAsync($"/events/{created.Id}/transition", new { to = "NotARealStatus" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_event_is_allowed_for_admin_but_not_official()
    {
        var created = await CreateEventAsync(40, "Deletable");

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var forbidden = await officialClient.DeleteAsync($"/events/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var response = await _admin.DeleteAsync($"/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var listed = await _admin.GetFromJsonAsync<List<EventResponse>>($"/events?competitionId={_competitionId}");
        Assert.DoesNotContain(listed!, e => e.Id == created.Id);
    }

    [Fact]
    public async Task Deleting_an_unknown_event_returns_404()
    {
        var response = await _admin.DeleteAsync($"/events/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_event_also_removes_its_participants_and_squads()
    {
        var created = await CreateEventAsync(41, "Deletable With Participants");

        var shooterResponse = await _admin.PostAsJsonAsync("/shooters", new { firstName = "To", lastName = "Delete" });
        shooterResponse.EnsureSuccessStatusCode();
        var shooter = await shooterResponse.Content.ReadFromJsonAsync<ShooterResponse>();

        var participantResponse = await _admin.PostAsJsonAsync(
            $"/events/{created.Id}/participants", new { shooterId = shooter!.Id });
        participantResponse.EnsureSuccessStatusCode();

        var squadResponse = await _admin.PostAsJsonAsync($"/events/{created.Id}/squads", new { });
        squadResponse.EnsureSuccessStatusCode();

        var response = await _admin.DeleteAsync($"/events/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The event itself is gone, so anything scoped to it now reports not-found rather
        // than stale participant/squad rows that should have been cascaded away with it.
        var participantsAfter = await _admin.GetAsync($"/events/{created.Id}/participants");
        Assert.Equal(HttpStatusCode.NotFound, participantsAfter.StatusCode);
    }

    private async Task<EventResponse> CreateEventAsync(int eventNumber, string name)
    {
        var response = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber, name, eventDate = "2040-01-07" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
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
