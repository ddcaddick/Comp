using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Competitions;
using Comp.Contracts.Events;
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
/// Drives the runner, run-saving, and entry-session endpoints — the M7 live-entry surface.
/// Covers the round-robin "next outstanding" advance (finish everyone's run 1 before
/// starting run 2), idempotent replay, overwriting a time, and the finalised-event lock.
/// </summary>
public class LiveEntryEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _admin = null!;
    private HttpClient _official = null!;
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
        _official = await AuthenticatedClientAsync(Roles.Official);

        using var superAdmin = await AuthenticatedClientAsync(Roles.SuperAdmin);
        var competitionResponse = await superAdmin.PostAsJsonAsync("/competitions",
            new { name = "M7 Season", year = 2042, startsOn = "2042-01-01", endsOn = "2042-12-31" });
        competitionResponse.EnsureSuccessStatusCode();
        _competitionId = (await competitionResponse.Content.ReadFromJsonAsync<CompetitionResponse>())!.Id;
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _official.Dispose();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Runner_shows_outstanding_runs_for_every_participant_before_anything_is_recorded()
    {
        var (@event, squad, p1, _) = await SetUpSquadOfTwoAsync(1);

        var runner = await _admin.GetFromJsonAsync<SquadRunnerResponse>($"/events/{@event.Id}/squads/{squad.Id}/runner");

        Assert.Equal(2, runner!.Participants.Count);
        var first = runner.Participants.Single(p => p.ParticipantId == p1.Id);
        Assert.Equal(2, first.Runs.Count); // RunsPerShooter default
        Assert.All(first.Runs, r => Assert.False(r.IsRecorded));
    }

    [Fact]
    public async Task Runner_for_an_unknown_event_or_squad_returns_404()
    {
        var (@event, squad, _, _) = await SetUpSquadOfTwoAsync(2);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.GetAsync($"/events/{Guid.NewGuid()}/squads/{squad.Id}/runner")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.GetAsync($"/events/{@event.Id}/squads/{Guid.NewGuid()}/runner")).StatusCode);
    }

    [Fact]
    public async Task Saving_runs_advances_round_robin_finishing_run_one_before_starting_run_two()
    {
        var (@event, _, p1, p2) = await SetUpSquadOfTwoAsync(3);

        // Round 1: p1 then p2.
        var save1 = await SaveRunAsync(@event.Id, p1.Id, 1, rawTimeMs: 90_000);
        Assert.Equal(p2.Id, save1.NextOutstanding!.ParticipantId);

        var save2 = await SaveRunAsync(@event.Id, p2.Id, 1, rawTimeMs: 91_000);
        // Everyone's done run 1 — advances to round 2, starting from p1.
        Assert.Equal(p1.Id, save2.NextOutstanding!.ParticipantId);

        // Round 2: p1 then p2.
        var save3 = await SaveRunAsync(@event.Id, p1.Id, 2, rawTimeMs: 89_000);
        Assert.Equal(p2.Id, save3.NextOutstanding!.ParticipantId);

        var save4 = await SaveRunAsync(@event.Id, p2.Id, 2, rawTimeMs: 90_500);
        Assert.Null(save4.NextOutstanding); // squad fully entered
    }

    [Fact]
    public async Task Recording_a_dnf_needs_no_raw_time_but_a_non_dnf_run_does()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(4);

        var dnf = await _official.PutAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}/runs/1",
            new { rawTimeMs = (int?)null, penaltyCount = 0, isDnf = true });
        dnf.EnsureSuccessStatusCode();
        var dnfBody = await dnf.Content.ReadFromJsonAsync<SaveRunResponse>();
        Assert.True(dnfBody!.SavedRun.IsDnf);
        Assert.Null(dnfBody.SavedRun.RawTimeMs);

        var missingTime = await _official.PutAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}/runs/2",
            new { rawTimeMs = (int?)null, penaltyCount = 0, isDnf = false });
        Assert.Equal(HttpStatusCode.BadRequest, missingTime.StatusCode);
    }

    [Fact]
    public async Task Overwriting_an_existing_run_updates_it_in_place()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(5);

        await SaveRunAsync(@event.Id, p1.Id, 1, rawTimeMs: 100_000);
        var overwritten = await SaveRunAsync(@event.Id, p1.Id, 1, rawTimeMs: 95_000);

        Assert.Equal(95_000, overwritten.SavedRun.RawTimeMs);

        var runner = await _admin.GetFromJsonAsync<SquadRunnerResponse>(
            $"/events/{@event.Id}/squads/{(await GetSquadIdAsync(@event.Id))}/runner");
        var run1 = runner!.Participants.Single(p => p.ParticipantId == p1.Id).Runs.Single(r => r.RunNumber == 1);
        Assert.Equal(95_000, run1.RawTimeMs); // one row, not two
    }

    [Fact]
    public async Task An_idempotency_key_replays_the_original_save_instead_of_applying_a_second_time()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(6);
        var key = Guid.NewGuid().ToString();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/events/{@event.Id}/participants/{p1.Id}/runs/1")
        {
            Content = JsonContent.Create(new { rawTimeMs = 100_000, penaltyCount = 0, isDnf = false })
        };
        request.Headers.Add("Idempotency-Key", key);
        var first = await _official.SendAsync(request);
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<SaveRunResponse>();

        // A retried request with the same key but different (buggy client) data — the
        // original result is replayed, not re-applied.
        var retry = new HttpRequestMessage(HttpMethod.Put, $"/events/{@event.Id}/participants/{p1.Id}/runs/1")
        {
            Content = JsonContent.Create(new { rawTimeMs = 999_999, penaltyCount = 5, isDnf = false })
        };
        retry.Headers.Add("Idempotency-Key", key);
        var second = await _official.SendAsync(retry);
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<SaveRunResponse>();

        Assert.Equal(firstBody!.SavedRun.RawTimeMs, secondBody!.SavedRun.RawTimeMs);
        Assert.Equal(100_000, secondBody.SavedRun.RawTimeMs);
    }

    [Fact]
    public async Task Saving_a_run_number_beyond_runs_per_shooter_returns_a_conflict()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(7);

        var response = await _official.PutAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}/runs/3",
            new { rawTimeMs = 100_000, penaltyCount = 0, isDnf = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Saving_a_run_for_an_unknown_event_or_participant_returns_404()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(8);

        var unknownEvent = await _official.PutAsJsonAsync($"/events/{Guid.NewGuid()}/participants/{p1.Id}/runs/1",
            new { rawTimeMs = 100_000, penaltyCount = 0, isDnf = false });
        Assert.Equal(HttpStatusCode.NotFound, unknownEvent.StatusCode);

        var unknownParticipant = await _official.PutAsJsonAsync(
            $"/events/{@event.Id}/participants/{Guid.NewGuid()}/runs/1",
            new { rawTimeMs = 100_000, penaltyCount = 0, isDnf = false });
        Assert.Equal(HttpStatusCode.NotFound, unknownParticipant.StatusCode);
    }

    [Fact]
    public async Task Saving_a_run_is_forbidden_for_read_only()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(9);

        using var readOnlyClient = await AuthenticatedClientAsync(Roles.ReadOnly);
        var response = await readOnlyClient.PutAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}/runs/1",
            new { rawTimeMs = 100_000, penaltyCount = 0, isDnf = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_finalised_event_locks_run_recording_even_though_nothing_can_reach_that_status_yet()
    {
        var (@event, _, p1, _) = await SetUpSquadOfTwoAsync(10);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CompDbContext>();
        var domainEvent = await dbContext.Events.SingleAsync(e => e.Id == @event.Id);
        domainEvent.Status = EventStatus.Finalised;
        dbContext.PendingActorOverride = domainEvent.CreatedByUserId;
        await dbContext.SaveChangesAsync();

        var response = await _official.PutAsJsonAsync($"/events/{@event.Id}/participants/{p1.Id}/runs/1",
            new { rawTimeMs = 100_000, penaltyCount = 0, isDnf = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Entry_session_reflects_the_most_recent_heartbeat_and_is_null_before_the_first_one()
    {
        var (@event, _, _, _) = await SetUpSquadOfTwoAsync(11);

        var before = await _admin.GetFromJsonAsync<EntrySessionResponse>($"/events/{@event.Id}/entry-session");
        Assert.Null(before!.UserId);
        Assert.Null(before.LastSeenAt);

        var heartbeat = await _official.PostAsync($"/events/{@event.Id}/entry-session/heartbeat", content: null);
        heartbeat.EnsureSuccessStatusCode();
        var heartbeatBody = await heartbeat.Content.ReadFromJsonAsync<EntrySessionResponse>();
        Assert.NotNull(heartbeatBody!.UserId);
        Assert.Equal("OFFICIAL", heartbeatBody.DisplayName);

        var after = await _admin.GetFromJsonAsync<EntrySessionResponse>($"/events/{@event.Id}/entry-session");
        Assert.Equal(heartbeatBody.UserId, after!.UserId);
    }

    [Fact]
    public async Task Entry_session_for_an_unknown_event_returns_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/events/{Guid.NewGuid()}/entry-session")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _official.PostAsync($"/events/{Guid.NewGuid()}/entry-session/heartbeat", content: null)).StatusCode);
    }

    private async Task<SaveRunResponse> SaveRunAsync(Guid eventId, Guid participantId, int runNumber, int rawTimeMs)
    {
        var response = await _official.PutAsJsonAsync($"/events/{eventId}/participants/{participantId}/runs/{runNumber}",
            new { rawTimeMs, penaltyCount = 0, isDnf = false });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SaveRunResponse>())!;
    }

    private async Task<Guid> GetSquadIdAsync(Guid eventId)
    {
        var squads = await _admin.GetFromJsonAsync<List<SquadResponse>>($"/events/{eventId}/squads");
        return squads!.Single().Id;
    }

    private async Task<(EventResponse Event, SquadResponse Squad, EventParticipantResponse P1, EventParticipantResponse P2)>
        SetUpSquadOfTwoAsync(int eventNumber)
    {
        var eventResponse = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber, name = $"Event {eventNumber}", eventDate = "2042-01-07" });
        eventResponse.EnsureSuccessStatusCode();
        var @event = (await eventResponse.Content.ReadFromJsonAsync<EventResponse>())!;

        var squadResponse = await _official.PostAsJsonAsync($"/events/{@event.Id}/squads", new { });
        squadResponse.EnsureSuccessStatusCode();
        var squad = (await squadResponse.Content.ReadFromJsonAsync<SquadResponse>())!;

        var shooter1 = await CreateShooterAsync($"First{eventNumber}", "Shooter");
        var shooter2 = await CreateShooterAsync($"Second{eventNumber}", "Shooter");

        var p1Response = await _official.PostAsJsonAsync($"/events/{@event.Id}/participants",
            new { shooterId = shooter1.Id, squadId = squad.Id });
        p1Response.EnsureSuccessStatusCode();
        var p1 = (await p1Response.Content.ReadFromJsonAsync<EventParticipantResponse>())!;

        var p2Response = await _official.PostAsJsonAsync($"/events/{@event.Id}/participants",
            new { shooterId = shooter2.Id, squadId = squad.Id });
        p2Response.EnsureSuccessStatusCode();
        var p2 = (await p2Response.Content.ReadFromJsonAsync<EventParticipantResponse>())!;

        return (@event, squad, p1, p2);
    }

    private async Task<ShooterResponse> CreateShooterAsync(string firstName, string lastName)
    {
        var response = await _admin.PostAsJsonAsync("/shooters", new { firstName, lastName });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShooterResponse>())!;
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
