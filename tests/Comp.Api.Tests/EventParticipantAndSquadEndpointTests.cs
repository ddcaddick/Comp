using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Competitions;
using Comp.Contracts.Events;
using Comp.Contracts.Leagues;
using Comp.Contracts.Shooters;
using Comp.Domain.Enums;
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
/// Drives /events/{id}/participants and /events/{id}/squads: managing them allows Super
/// Admin/Admin/Official (not Read-only); a participant's league is snapshotted from their
/// current membership at add time; moving between squads auto-appends unless a position
/// is given; and a finalised event locks all of it, even though nothing in M6 can reach
/// Finalised through the API yet (set directly here to prove the lock itself).
/// </summary>
public class EventParticipantAndSquadEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _admin = null!;
    private HttpClient _superAdmin = null!;
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
        _superAdmin = await AuthenticatedClientAsync(Roles.SuperAdmin);

        // Managing competitions and leagues is Super Admin only.
        var competitionResponse = await _superAdmin.PostAsJsonAsync("/competitions",
            new { name = "M6 Participant Season", year = 2041, startsOn = "2041-01-01", endsOn = "2041-12-31" });
        competitionResponse.EnsureSuccessStatusCode();
        _competitionId = (await competitionResponse.Content.ReadFromJsonAsync<CompetitionResponse>())!.Id;
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _superAdmin.Dispose();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Adding_a_participant_is_allowed_for_official_but_not_read_only()
    {
        var @event = await CreateEventAsync(1);
        var shooter = await CreateShooterAsync("Ada", "Lovelace");

        using var readOnlyClient = await AuthenticatedClientAsync(Roles.ReadOnly);
        var forbidden = await readOnlyClient.PostAsJsonAsync($"/events/{@event.Id}/participants",
            new { shooterId = shooter.Id });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var response = await officialClient.PostAsJsonAsync($"/events/{@event.Id}/participants",
            new { shooterId = shooter.Id });
        response.EnsureSuccessStatusCode();
        var participant = await response.Content.ReadFromJsonAsync<EventParticipantResponse>();
        Assert.Equal(shooter.Id, participant!.ShooterId);
        Assert.Null(participant.SquadId);
        Assert.Null(participant.LeagueId); // no league membership set up for this shooter
    }

    [Fact]
    public async Task Adding_a_participant_snapshots_their_current_league_membership()
    {
        var @event = await CreateEventAsync(2);
        var shooter = await CreateShooterAsync("Grace", "Hopper");
        var league = await CreateLeagueAsync("Division A", tier: 1);
        await _superAdmin.PutAsJsonAsync($"/leagues/{league.Id}/members", new { shooterIds = new[] { shooter.Id } });

        var response = await _admin.PostAsJsonAsync($"/events/{@event.Id}/participants", new { shooterId = shooter.Id });
        response.EnsureSuccessStatusCode();
        var participant = await response.Content.ReadFromJsonAsync<EventParticipantResponse>();

        Assert.Equal(league.Id, participant!.LeagueId);
    }

    [Fact]
    public async Task Adding_an_unknown_or_deactivated_shooter_returns_a_conflict()
    {
        var @event = await CreateEventAsync(3);

        var unknown = await _admin.PostAsJsonAsync($"/events/{@event.Id}/participants", new { shooterId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);

        var shooter = await CreateShooterAsync("Deactivated", "Shooter");
        await _admin.PostAsync($"/shooters/{shooter.Id}/deactivate", content: null);

        var deactivated = await _admin.PostAsJsonAsync($"/events/{@event.Id}/participants", new { shooterId = shooter.Id });
        Assert.Equal(HttpStatusCode.Conflict, deactivated.StatusCode);
    }

    [Fact]
    public async Task Adding_the_same_shooter_twice_returns_a_conflict()
    {
        var @event = await CreateEventAsync(4);
        var shooter = await CreateShooterAsync("Once", "Only");

        await _admin.PostAsJsonAsync($"/events/{@event.Id}/participants", new { shooterId = shooter.Id });
        var second = await _admin.PostAsJsonAsync($"/events/{@event.Id}/participants", new { shooterId = shooter.Id });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Adding_to_an_unknown_event_returns_404()
    {
        var shooter = await CreateShooterAsync("No", "Event");
        var response = await _admin.PostAsJsonAsync($"/events/{Guid.NewGuid()}/participants", new { shooterId = shooter.Id });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Moving_a_participant_to_a_squad_auto_appends_and_an_explicit_position_is_honoured()
    {
        var @event = await CreateEventAsync(5);
        var squad = await CreateSquadAsync(@event.Id);
        var shooter1 = await CreateShooterAsync("First", "In");
        var shooter2 = await CreateShooterAsync("Second", "In");

        var p1 = await AddParticipantAsync(@event.Id, shooter1.Id);
        var p2 = await AddParticipantAsync(@event.Id, shooter2.Id);

        var move1 = await _admin.PatchAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}", new { squadId = squad.Id });
        move1.EnsureSuccessStatusCode();
        Assert.Equal(1, (await move1.Content.ReadFromJsonAsync<EventParticipantResponse>())!.PositionInSquad);

        var move2 = await _admin.PatchAsJsonAsync($"/events/{@event.Id}/participants/{p2.Id}", new { squadId = squad.Id });
        move2.EnsureSuccessStatusCode();
        Assert.Equal(2, (await move2.Content.ReadFromJsonAsync<EventParticipantResponse>())!.PositionInSquad);

        // Explicit position is honoured as-is (no renumbering of others — see the
        // architecture doc's "what is deliberately absent": a duplicated position is a
        // display-order glitch, not a corrupted result).
        var reorder = await _admin.PatchAsJsonAsync($"/events/{@event.Id}/participants/{p2.Id}",
            new { squadId = squad.Id, positionInSquad = 1 });
        reorder.EnsureSuccessStatusCode();
        Assert.Equal(1, (await reorder.Content.ReadFromJsonAsync<EventParticipantResponse>())!.PositionInSquad);

        // Unassigning clears both fields.
        var unassign = await _admin.PatchAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}", new { squadId = (Guid?)null });
        unassign.EnsureSuccessStatusCode();
        var unassigned = await unassign.Content.ReadFromJsonAsync<EventParticipantResponse>();
        Assert.Null(unassigned!.SquadId);
        Assert.Null(unassigned.PositionInSquad);
    }

    [Fact]
    public async Task Moving_a_participant_to_a_squad_from_a_different_event_returns_a_conflict()
    {
        var eventA = await CreateEventAsync(6);
        var eventB = await CreateEventAsync(7);
        var squadInB = await CreateSquadAsync(eventB.Id);
        var shooter = await CreateShooterAsync("Wrong", "Event");
        var participant = await AddParticipantAsync(eventA.Id, shooter.Id);

        var response = await _admin.PatchAsJsonAsync(
            $"/events/{eventA.Id}/participants/{participant.Id}", new { squadId = squadInB.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Removing_a_participant_with_no_runs_succeeds_and_an_unknown_one_returns_404()
    {
        var @event = await CreateEventAsync(8);
        var shooter = await CreateShooterAsync("Removable", "Person");
        var participant = await AddParticipantAsync(@event.Id, shooter.Id);

        var remove = await _admin.DeleteAsync($"/events/{@event.Id}/participants/{participant.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var listAfter = await _admin.GetFromJsonAsync<List<EventParticipantResponse>>($"/events/{@event.Id}/participants");
        Assert.DoesNotContain(listAfter!, p => p.Id == participant.Id);

        var removeUnknown = await _admin.DeleteAsync($"/events/{@event.Id}/participants/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, removeUnknown.StatusCode);
    }

    [Fact]
    public async Task Creating_a_squad_is_allowed_for_official_and_auto_numbers_when_omitted()
    {
        var @event = await CreateEventAsync(9);

        using var readOnlyClient = await AuthenticatedClientAsync(Roles.ReadOnly);
        var forbidden = await readOnlyClient.PostAsJsonAsync($"/events/{@event.Id}/squads", new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var first = await officialClient.PostAsJsonAsync($"/events/{@event.Id}/squads", new { name = "Squad One" });
        first.EnsureSuccessStatusCode();
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<SquadResponse>())!.SquadNumber);

        var second = await officialClient.PostAsJsonAsync($"/events/{@event.Id}/squads", new { name = "Squad Two" });
        second.EnsureSuccessStatusCode();
        Assert.Equal(2, (await second.Content.ReadFromJsonAsync<SquadResponse>())!.SquadNumber);
    }

    [Fact]
    public async Task Creating_a_squad_with_a_duplicate_number_returns_a_conflict()
    {
        var @event = await CreateEventAsync(10);
        await _admin.PostAsJsonAsync($"/events/{@event.Id}/squads", new { squadNumber = 1 });

        var duplicate = await _admin.PostAsJsonAsync($"/events/{@event.Id}/squads", new { squadNumber = 1 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Listing_squads_and_participants_for_an_unknown_event_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/events/{Guid.NewGuid()}/squads")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/events/{Guid.NewGuid()}/participants")).StatusCode);
    }

    [Fact]
    public async Task A_finalised_event_locks_participants_and_squads_even_though_nothing_can_reach_that_status_yet()
    {
        var @event = await CreateEventAsync(11);
        var shooter = await CreateShooterAsync("Locked", "Out");
        var participant = await AddParticipantAsync(@event.Id, shooter.Id);

        await SetEventStatusDirectlyAsync(@event.Id, EventStatus.Finalised);

        var addAnother = await _admin.PostAsJsonAsync($"/events/{@event.Id}/participants",
            new { shooterId = (await CreateShooterAsync("Also", "Locked")).Id });
        Assert.Equal(HttpStatusCode.Conflict, addAnother.StatusCode);

        var update = await _admin.PatchAsJsonAsync($"/events/{@event.Id}/participants/{participant.Id}", new { squadId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);

        var remove = await _admin.DeleteAsync($"/events/{@event.Id}/participants/{participant.Id}");
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);

        var addSquad = await _admin.PostAsJsonAsync($"/events/{@event.Id}/squads", new { });
        Assert.Equal(HttpStatusCode.Conflict, addSquad.StatusCode);
    }

    private async Task SetEventStatusDirectlyAsync(Guid eventId, EventStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CompDbContext>();
        var @event = await dbContext.Events.SingleAsync(e => e.Id == eventId);
        @event.Status = status;
        dbContext.PendingActorOverride = @event.CreatedByUserId;
        await dbContext.SaveChangesAsync();
    }

    private async Task<EventResponse> CreateEventAsync(int eventNumber)
    {
        var response = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber, name = $"Event {eventNumber}", eventDate = "2041-01-07" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventResponse>())!;
    }

    private async Task<ShooterResponse> CreateShooterAsync(string firstName, string lastName)
    {
        var response = await _admin.PostAsJsonAsync("/shooters", new { firstName, lastName });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShooterResponse>())!;
    }

    private async Task<LeagueResponse> CreateLeagueAsync(string name, int tier)
    {
        var response = await _superAdmin.PostAsJsonAsync($"/competitions/{_competitionId}/leagues", new { name, tier });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LeagueResponse>())!;
    }

    private async Task<SquadResponse> CreateSquadAsync(Guid eventId)
    {
        var response = await _admin.PostAsJsonAsync($"/events/{eventId}/squads", new { });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SquadResponse>())!;
    }

    private async Task<EventParticipantResponse> AddParticipantAsync(Guid eventId, Guid shooterId)
    {
        var response = await _admin.PostAsJsonAsync($"/events/{eventId}/participants", new { shooterId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventParticipantResponse>())!;
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
