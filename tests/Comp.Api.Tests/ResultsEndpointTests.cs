using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Competitions;
using Comp.Contracts.Events;
using Comp.Contracts.Leagues;
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
/// Drives M8 end to end: finalising computes and freezes event_results via Comp.Scoring,
/// results are live/provisional before that and frozen after, amending unlocks with a
/// reason and clears the frozen rows, and standings aggregate finalised events for a
/// league. Per the architecture doc's testing strategy: "an official must not finalise; an
/// admin without the claim must not amend."
/// </summary>
public class ResultsEndpointTests : IAsyncLifetime
{
    private const string Password = "Correct horse battery staple 9!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private HttpClient _admin = null!;
    private HttpClient _official = null!;
    private HttpClient _superAdmin = null!;
    private Guid _competitionId;
    private Guid _leagueId;

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
            // Only the Super Admin test user holds the amend-published privilege — an
            // Admin without it is exactly the negative case the doc's testing strategy
            // calls out ("an admin without the claim must not amend").
            var user = new AppUser
            {
                UserName = email, Email = email, EmailConfirmed = true, DisplayName = role,
                CanAmendPublished = role == Roles.SuperAdmin
            };
            dbContext.PendingActorOverride = user.Id;
            await userManager.CreateAsync(user, Password);
            dbContext.PendingActorOverride = user.Id;
            await userManager.AddToRoleAsync(user, role);
        }

        _admin = await AuthenticatedClientAsync(Roles.Admin);
        _official = await AuthenticatedClientAsync(Roles.Official);
        _superAdmin = await AuthenticatedClientAsync(Roles.SuperAdmin);

        var competitionResponse = await _superAdmin.PostAsJsonAsync("/competitions",
            new { name = "M8 Season", year = 2042, startsOn = "2042-01-01", endsOn = "2042-12-31" });
        competitionResponse.EnsureSuccessStatusCode();
        _competitionId = (await competitionResponse.Content.ReadFromJsonAsync<CompetitionResponse>())!.Id;

        var leagueResponse = await _superAdmin.PostAsJsonAsync($"/competitions/{_competitionId}/leagues",
            new
            {
                name = "Division A", tier = 1,
                pointsForFirst = (int?)null, pointsDecrement = (int?)null,
                dropWorstCount = (int?)null, absencesCountAsZero = (bool?)null
            });
        leagueResponse.EnsureSuccessStatusCode();
        _leagueId = (await leagueResponse.Content.ReadFromJsonAsync<LeagueResponse>())!.Id;
    }

    public async Task DisposeAsync()
    {
        _admin.Dispose();
        _official.Dispose();
        _superAdmin.Dispose();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Results_are_live_and_provisional_before_finalising_then_frozen_after()
    {
        var (@event, faster, slower) = await SetUpEventInReviewAsync(1, fasterMs: 90_000, slowerMs: 95_000);

        var live = await GetResultsAsync(@event.Id);
        Assert.False(live!.IsFinal);
        Assert.Equal("Review", live.EventStatus);
        AssertRanking(live, faster.Id, slower.Id);

        var finalise = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" });
        finalise.EnsureSuccessStatusCode();

        var frozen = await GetResultsAsync(@event.Id);
        Assert.True(frozen!.IsFinal);
        Assert.Equal("Finalised", frozen.EventStatus);
        AssertRanking(frozen, faster.Id, slower.Id);
    }

    [Fact]
    public async Task A_participant_with_no_runs_recorded_shows_as_not_run_rather_than_dnf()
    {
        var (@event, faster, slower) = await SetUpEventInReviewAsync(30, fasterMs: 90_000, slowerMs: 95_000);

        // Added after the event was already advanced through Setup/InProgress/Review by
        // SetUpEventInReviewAsync, mirroring a shooter who simply never got called up —
        // never had a run entered for them at all, as opposed to one who was up and DNF'd.
        var neverUpShooter = await CreateShooterAsync("NeverUp", "Shooter");
        await _superAdmin.PutAsJsonAsync($"/leagues/{_leagueId}/members",
            new { shooterIds = new[] { faster.ShooterId, slower.ShooterId, neverUpShooter.Id } });
        var neverUp = await AddParticipantAsync(@event.Id, neverUpShooter.Id);

        var live = await GetResultsAsync(@event.Id);
        var liveNeverUp = live!.Participants.Single(p => p.ParticipantId == neverUp.Id);
        Assert.Equal("NotRun", liveNeverUp.Status);
        Assert.Null(liveNeverUp.EventTimeMs);
        Assert.Null(liveNeverUp.OverallPosition);
        Assert.Null(liveNeverUp.LeaguePosition);

        var finalise = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" });
        finalise.EnsureSuccessStatusCode();

        var frozen = await GetResultsAsync(@event.Id);
        var frozenNeverUp = frozen!.Participants.Single(p => p.ParticipantId == neverUp.Id);
        Assert.Equal("NotRun", frozenNeverUp.Status);
        Assert.Null(frozenNeverUp.EventTimeMs);
        Assert.Null(frozenNeverUp.OverallPosition);
        Assert.Null(frozenNeverUp.LeaguePosition);
    }

    [Fact]
    public async Task Finalising_computes_correct_overall_and_league_points()
    {
        var (@event, faster, slower) = await SetUpEventInReviewAsync(2, fasterMs: 90_000, slowerMs: 95_000);

        var finalise = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" });
        finalise.EnsureSuccessStatusCode();

        var results = await GetResultsAsync(@event.Id);
        var first = results!.Participants.Single(p => p.ParticipantId == faster.Id);
        var second = results.Participants.Single(p => p.ParticipantId == slower.Id);

        Assert.Equal("Ranked", first.Status);
        Assert.Equal(90_000, first.EventTimeMs);
        Assert.Equal(1, first.BestRunNumber);
        Assert.Equal(1, first.OverallPosition);
        Assert.Equal(1, first.LeaguePosition);
        Assert.Equal(50, first.LeaguePoints); // default PointsForFirst
        Assert.Equal("Division A", first.LeagueName);

        Assert.Equal(2, second.OverallPosition);
        Assert.Equal(2, second.LeaguePosition);
        Assert.Equal(49, second.LeaguePoints); // 50 - (2-1)*1
    }

    [Fact]
    public async Task Finalising_is_only_allowed_from_review_and_only_for_super_admin_or_admin()
    {
        var (@event, _, _) = await SetUpEventInReviewAsync(3, fasterMs: 90_000, slowerMs: 95_000);

        // Step back to InProgress so Finalised is no longer one step away.
        var back = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "InProgress" });
        back.EnsureSuccessStatusCode();

        var tooEarly = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" });
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);

        var forward = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Review" });
        forward.EnsureSuccessStatusCode();

        var forbidden = await _official.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var ok = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" });
        ok.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Amend_requires_role_and_the_amend_published_claim_and_a_reason()
    {
        var (@event, _, _) = await SetUpEventInReviewAsync(4, fasterMs: 90_000, slowerMs: 95_000);
        (await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" })).EnsureSuccessStatusCode();

        var officialForbidden = await _official.PostAsJsonAsync($"/events/{@event.Id}/amend", new { reason = "Timing error" });
        Assert.Equal(HttpStatusCode.Forbidden, officialForbidden.StatusCode);

        // Admin holds the role but not the claim in this test's setup.
        var adminForbidden = await _admin.PostAsJsonAsync($"/events/{@event.Id}/amend", new { reason = "Timing error" });
        Assert.Equal(HttpStatusCode.Forbidden, adminForbidden.StatusCode);

        var missingReason = await _superAdmin.PostAsJsonAsync($"/events/{@event.Id}/amend", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        var ok = await _superAdmin.PostAsJsonAsync($"/events/{@event.Id}/amend", new { reason = "Timing error on run 1" });
        ok.EnsureSuccessStatusCode();
        Assert.Equal("Review", (await ok.Content.ReadFromJsonAsync<EventResponse>())!.Status);
    }

    [Fact]
    public async Task Amending_a_result_and_correcting_it_visibly_changes_the_standings()
    {
        var (@event, faster, slower) = await SetUpEventInReviewAsync(5, fasterMs: 90_000, slowerMs: 95_000);
        (await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" })).EnsureSuccessStatusCode();

        var before = await GetStandingsAsync();
        Assert.Equal(50, before!.Standings.Single(s => s.ShooterId == faster.ShooterId).RunningTotal);
        Assert.Equal(49, before.Standings.Single(s => s.ShooterId == slower.ShooterId).RunningTotal);

        // Amend, then discover the "slower" shooter actually beat the "faster" one.
        (await _superAdmin.PostAsJsonAsync($"/events/{@event.Id}/amend", new { reason = "Swapped watches" }))
            .EnsureSuccessStatusCode();

        var correctedSlower = await _official.PutAsJsonAsync(
            $"/events/{@event.Id}/participants/{slower.Id}/runs/1",
            new { rawTimeMs = 80_000, penaltyCount = 0, isDnf = false });
        correctedSlower.EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to = "Finalised" })).EnsureSuccessStatusCode();

        var after = await GetStandingsAsync();
        Assert.Equal(1, after!.Standings.Single(s => s.ShooterId == slower.ShooterId).Position);
        Assert.Equal(50, after.Standings.Single(s => s.ShooterId == slower.ShooterId).RunningTotal);
        Assert.Equal(2, after.Standings.Single(s => s.ShooterId == faster.ShooterId).Position);
        Assert.Equal(49, after.Standings.Single(s => s.ShooterId == faster.ShooterId).RunningTotal);
    }

    [Fact]
    public async Task Standings_are_provisional_until_events_held_exceeds_the_drop_count()
    {
        var dropLeague = await _superAdmin.PostAsJsonAsync($"/competitions/{_competitionId}/leagues",
            new { name = "Division B", tier = 2, pointsForFirst = (int?)null, pointsDecrement = (int?)null, dropWorstCount = 2, absencesCountAsZero = (bool?)null });
        dropLeague.EnsureSuccessStatusCode();
        var leagueId = (await dropLeague.Content.ReadFromJsonAsync<LeagueResponse>())!.Id;

        var shooterA = await CreateShooterAsync("Drop", "A");
        var shooterB = await CreateShooterAsync("Drop", "B");
        await _superAdmin.PutAsJsonAsync($"/leagues/{leagueId}/members", new { shooterIds = new[] { shooterA.Id, shooterB.Id } });

        for (var eventNumber = 20; eventNumber <= 21; eventNumber++)
        {
            var eventResponse = await _admin.PostAsJsonAsync("/events",
                new { competitionId = _competitionId, eventNumber, name = $"Drop test {eventNumber}", eventDate = "2042-02-01" });
            eventResponse.EnsureSuccessStatusCode();
            var @event = (await eventResponse.Content.ReadFromJsonAsync<EventResponse>())!;

            var participantA = await AddParticipantAsync(@event.Id, shooterA.Id);
            var participantB = await AddParticipantAsync(@event.Id, shooterB.Id);
            await SaveRunAsync(@event.Id, participantA.Id, 90_000);
            await SaveRunAsync(@event.Id, participantB.Id, 95_000);

            foreach (var to in new[] { "Setup", "InProgress", "Review", "Finalised" })
            {
                (await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to })).EnsureSuccessStatusCode();
            }
        }

        var standingsResponse = await _admin.GetFromJsonAsync<LeagueStandingsResponse>($"/leagues/{leagueId}/standings");
        Assert.Equal(2, standingsResponse!.EventsHeld);
        // D7: provisional stays true while eventsHeld <= dropWorstCount (2 <= 2 here) — a
        // 3rd event would push eventsHeld past dropWorstCount and start dropping.
        Assert.All(standingsResponse.Standings, s => Assert.True(s.IsProvisional));
        Assert.All(standingsResponse.Standings, s => Assert.Equal(s.RunningTotal, s.CountingTotal));
    }

    [Fact]
    public async Task Standings_report_missed_events_separately_from_the_drop_rule()
    {
        // A missed event and a "dropped for scoring" event are different things: a shooter
        // who attends every event (even DNFing one) has MissedEvents = 0 even though the
        // drop rule still excludes their worst result, while a shooter who skips an event
        // entirely has MissedEvents = 1 regardless of whether that specific event ends up
        // being the one dropped for scoring.
        var dropLeague = await _superAdmin.PostAsJsonAsync($"/competitions/{_competitionId}/leagues",
            new { name = "Division C", tier = 3, pointsForFirst = (int?)null, pointsDecrement = (int?)null, dropWorstCount = 1, absencesCountAsZero = (bool?)null });
        dropLeague.EnsureSuccessStatusCode();
        var leagueId = (await dropLeague.Content.ReadFromJsonAsync<LeagueResponse>())!.Id;

        var shooterA = await CreateShooterAsync("Always", "There");
        var shooterB = await CreateShooterAsync("Sometimes", "Missing");
        await _superAdmin.PutAsJsonAsync($"/leagues/{leagueId}/members", new { shooterIds = new[] { shooterA.Id, shooterB.Id } });

        // Event 1: both attend with a valid time.
        var event1 = (await (await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 1, name = "Missed test 1", eventDate = "2042-03-01" }))
            .Content.ReadFromJsonAsync<EventResponse>())!;
        var event1A = await AddParticipantAsync(event1.Id, shooterA.Id);
        var event1B = await AddParticipantAsync(event1.Id, shooterB.Id);
        await SaveRunAsync(event1.Id, event1A.Id, 90_000);
        await SaveRunAsync(event1.Id, event1B.Id, 95_000);
        foreach (var to in new[] { "Setup", "InProgress", "Review", "Finalised" })
        {
            (await _admin.PostAsJsonAsync($"/events/{event1.Id}/transition", new { to })).EnsureSuccessStatusCode();
        }

        // Event 2: only A is entered at all -- B genuinely misses this one.
        var event2 = (await (await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 2, name = "Missed test 2", eventDate = "2042-03-08" }))
            .Content.ReadFromJsonAsync<EventResponse>())!;
        var event2A = await AddParticipantAsync(event2.Id, shooterA.Id);
        await SaveRunAsync(event2.Id, event2A.Id, 90_000);
        foreach (var to in new[] { "Setup", "InProgress", "Review", "Finalised" })
        {
            (await _admin.PostAsJsonAsync($"/events/{event2.Id}/transition", new { to })).EnsureSuccessStatusCode();
        }

        // Event 3: both attend, but A DNFs both runs -- A was there, just scored zero.
        var event3 = (await (await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber = 3, name = "Missed test 3", eventDate = "2042-03-15" }))
            .Content.ReadFromJsonAsync<EventResponse>())!;
        var event3A = await AddParticipantAsync(event3.Id, shooterA.Id);
        var event3B = await AddParticipantAsync(event3.Id, shooterB.Id);
        await _official.PutAsJsonAsync($"/events/{event3.Id}/participants/{event3A.Id}/runs/1",
            new { rawTimeMs = (int?)null, penaltyCount = 0, isDnf = true });
        await _official.PutAsJsonAsync($"/events/{event3.Id}/participants/{event3A.Id}/runs/2",
            new { rawTimeMs = (int?)null, penaltyCount = 0, isDnf = true });
        await SaveRunAsync(event3.Id, event3B.Id, 95_000);
        foreach (var to in new[] { "Setup", "InProgress", "Review", "Finalised" })
        {
            (await _admin.PostAsJsonAsync($"/events/{event3.Id}/transition", new { to })).EnsureSuccessStatusCode();
        }

        var standingsResponse = await _admin.GetFromJsonAsync<LeagueStandingsResponse>($"/leagues/{leagueId}/standings");
        Assert.Equal(3, standingsResponse!.EventsHeld);

        var standingA = standingsResponse.Standings.Single(s => s.ShooterId == shooterA.Id);
        var standingB = standingsResponse.Standings.Single(s => s.ShooterId == shooterB.Id);

        // A attended all three events (DNFing one doesn't count as missing it).
        Assert.Equal(0, standingA.MissedEvents);
        // B skipped event 2 entirely.
        Assert.Equal(1, standingB.MissedEvents);

        // Both still get exactly one event's points dropped (dropWorstCount = 1) -- for A
        // that's the DNF'd event 3 (0 points, A ranked 1st in the other two); for B it's
        // the padded zero for the missed event 2 (B ranked 2nd in event 1, 1st in event 3).
        // Either way the dropped entry is worth 0, so CountingTotal == RunningTotal here --
        // it's the MissedEvents count above that actually tells the two scenarios apart.
        Assert.False(standingA.IsProvisional);
        Assert.False(standingB.IsProvisional);
        Assert.Equal(100, standingA.RunningTotal); // 50 (event1, 1st) + 50 (event2, 1st) + 0 (event3, DNF)
        Assert.Equal(100, standingA.CountingTotal);
        Assert.Equal(99, standingB.RunningTotal); // 49 (event1, 2nd) + 0 (event2, missed/padded) + 50 (event3, 1st)
        Assert.Equal(99, standingB.CountingTotal);
    }

    private async Task<(EventResponse Event, EventParticipantResponse Faster, EventParticipantResponse Slower)>
        SetUpEventInReviewAsync(int eventNumber, int fasterMs, int slowerMs)
    {
        var eventResponse = await _admin.PostAsJsonAsync("/events",
            new { competitionId = _competitionId, eventNumber, name = $"Event {eventNumber}", eventDate = "2042-01-07" });
        eventResponse.EnsureSuccessStatusCode();
        var @event = (await eventResponse.Content.ReadFromJsonAsync<EventResponse>())!;

        var shooterFaster = await CreateShooterAsync($"Faster{eventNumber}", "Shooter");
        var shooterSlower = await CreateShooterAsync($"Slower{eventNumber}", "Shooter");
        await _superAdmin.PutAsJsonAsync($"/leagues/{_leagueId}/members", new { shooterIds = new[] { shooterFaster.Id, shooterSlower.Id } });

        var faster = await AddParticipantAsync(@event.Id, shooterFaster.Id);
        var slower = await AddParticipantAsync(@event.Id, shooterSlower.Id);

        await SaveRunAsync(@event.Id, faster.Id, fasterMs);
        await SaveRunAsync(@event.Id, slower.Id, slowerMs);

        foreach (var to in new[] { "Setup", "InProgress", "Review" })
        {
            var transition = await _admin.PostAsJsonAsync($"/events/{@event.Id}/transition", new { to });
            transition.EnsureSuccessStatusCode();
        }

        return (@event, faster, slower);
    }

    private static void AssertRanking(EventResultsResponse results, Guid fasterId, Guid slowerId)
    {
        var first = results.Participants.Single(p => p.ParticipantId == fasterId);
        var second = results.Participants.Single(p => p.ParticipantId == slowerId);
        Assert.Equal(1, first.OverallPosition);
        Assert.Equal(2, second.OverallPosition);
    }

    private async Task<EventResultsResponse?> GetResultsAsync(Guid eventId) =>
        await _admin.GetFromJsonAsync<EventResultsResponse>($"/events/{eventId}/results");

    private async Task<LeagueStandingsResponse?> GetStandingsAsync() =>
        await _admin.GetFromJsonAsync<LeagueStandingsResponse>($"/leagues/{_leagueId}/standings");

    private async Task<EventParticipantResponse> AddParticipantAsync(Guid eventId, Guid shooterId)
    {
        var response = await _official.PostAsJsonAsync($"/events/{eventId}/participants", new { shooterId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventParticipantResponse>())!;
    }

    private async Task SaveRunAsync(Guid eventId, Guid participantId, int rawTimeMs)
    {
        var response = await _official.PutAsJsonAsync(
            $"/events/{eventId}/participants/{participantId}/runs/1",
            new { rawTimeMs, penaltyCount = 0, isDnf = false });
        response.EnsureSuccessStatusCode();
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
