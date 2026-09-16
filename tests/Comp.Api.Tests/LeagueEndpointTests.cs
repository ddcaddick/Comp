using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comp.Contracts.Auth;
using Comp.Contracts.Competitions;
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
/// Drives /competitions/{id}/leagues and /leagues/{id}/members through the real HTTP
/// pipeline: creation, the (competition_id, tier)/(competition_id, name) uniqueness
/// guards, the 100-member cap (architecture doc D4), moving a shooter between leagues in
/// the same competition, and that a closed competition locks both.
/// </summary>
public class LeagueEndpointTests : IAsyncLifetime
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
    public async Task Creating_a_league_is_super_admin_only_and_leagues_list_by_tier()
    {
        var competition = await CreateCompetitionAsync("Division Season", 2031);

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var forbidden = await officialClient.PostAsJsonAsync($"/competitions/{competition.Id}/leagues",
            new { name = "Division B", tier = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await CreateLeagueAsync(competition.Id, "Division B", tier: 2);
        await CreateLeagueAsync(competition.Id, "Division A", tier: 1);

        var leagues = (await _superAdmin.GetFromJsonAsync<List<LeagueResponse>>($"/competitions/{competition.Id}/leagues"))!;
        Assert.Equal(["Division A", "Division B"], leagues.Select(l => l.Name));
        Assert.Equal(50, leagues[0].PointsForFirst);
    }

    [Fact]
    public async Task Creating_a_league_with_a_duplicate_tier_or_name_returns_a_conflict()
    {
        var competition = await CreateCompetitionAsync("Duplicate Test Season", 2032);
        await CreateLeagueAsync(competition.Id, "Division A", tier: 1);

        var duplicateTier = await _superAdmin.PostAsJsonAsync($"/competitions/{competition.Id}/leagues",
            new { name = "Different Name", tier = 1 });
        Assert.Equal(HttpStatusCode.Conflict, duplicateTier.StatusCode);

        var duplicateName = await _superAdmin.PostAsJsonAsync($"/competitions/{competition.Id}/leagues",
            new { name = "Division A", tier = 2 });
        Assert.Equal(HttpStatusCode.Conflict, duplicateName.StatusCode);
    }

    [Fact]
    public async Task Creating_a_league_under_an_unknown_competition_returns_404()
    {
        var response = await _superAdmin.PostAsJsonAsync($"/competitions/{Guid.NewGuid()}/leagues",
            new { name = "Division A", tier = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Setting_members_replaces_the_roster_and_is_readable_by_any_role()
    {
        var competition = await CreateCompetitionAsync("Membership Season", 2033);
        var league = await CreateLeagueAsync(competition.Id, "Division A", tier: 1);
        var ada = await CreateShooterAsync("Ada", "Lovelace");
        var grace = await CreateShooterAsync("Grace", "Hopper");

        var setResponse = await _superAdmin.PutAsJsonAsync($"/leagues/{league.Id}/members",
            new { shooterIds = new[] { ada.Id, grace.Id } });
        setResponse.EnsureSuccessStatusCode();

        using var readOnlyClient = await AuthenticatedClientAsync(Roles.ReadOnly);
        var members = await readOnlyClient.GetFromJsonAsync<List<LeagueMemberResponse>>($"/leagues/{league.Id}/members");
        Assert.Equal(2, members!.Count);
        Assert.Contains(members, m => m.ShooterId == ada.Id);
        Assert.Contains(members, m => m.ShooterId == grace.Id);

        // Dropping Grace from a second PUT removes her from the roster.
        await _superAdmin.PutAsJsonAsync($"/leagues/{league.Id}/members", new { shooterIds = new[] { ada.Id } });
        var updated = await readOnlyClient.GetFromJsonAsync<List<LeagueMemberResponse>>($"/leagues/{league.Id}/members");
        Assert.Single(updated!);
        Assert.Equal(ada.Id, updated![0].ShooterId);
    }

    [Fact]
    public async Task Setting_members_is_forbidden_for_official()
    {
        var competition = await CreateCompetitionAsync("Forbidden Membership Season", 2034);
        var league = await CreateLeagueAsync(competition.Id, "Division A", tier: 1);
        var shooter = await CreateShooterAsync("No", "Access");

        using var officialClient = await AuthenticatedClientAsync(Roles.Official);
        var response = await officialClient.PutAsJsonAsync($"/leagues/{league.Id}/members",
            new { shooterIds = new[] { shooter.Id } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_shooter_already_in_one_league_moves_rather_than_being_rejected()
    {
        var competition = await CreateCompetitionAsync("Move Season", 2035);
        var divisionA = await CreateLeagueAsync(competition.Id, "Division A", tier: 1);
        var divisionB = await CreateLeagueAsync(competition.Id, "Division B", tier: 2);
        var shooter = await CreateShooterAsync("Moves", "Around");

        await _superAdmin.PutAsJsonAsync($"/leagues/{divisionA.Id}/members", new { shooterIds = new[] { shooter.Id } });

        var moveResponse = await _superAdmin.PutAsJsonAsync($"/leagues/{divisionB.Id}/members",
            new { shooterIds = new[] { shooter.Id } });
        moveResponse.EnsureSuccessStatusCode();

        var stillInA = await _superAdmin.GetFromJsonAsync<List<LeagueMemberResponse>>($"/leagues/{divisionA.Id}/members");
        var nowInB = await _superAdmin.GetFromJsonAsync<List<LeagueMemberResponse>>($"/leagues/{divisionB.Id}/members");
        Assert.DoesNotContain(stillInA!, m => m.ShooterId == shooter.Id);
        Assert.Contains(nowInB!, m => m.ShooterId == shooter.Id);
    }

    [Fact]
    public async Task Setting_members_with_an_unknown_shooter_id_returns_a_conflict()
    {
        var competition = await CreateCompetitionAsync("Unknown Shooter Season", 2036);
        var league = await CreateLeagueAsync(competition.Id, "Division A", tier: 1);

        var response = await _superAdmin.PutAsJsonAsync($"/leagues/{league.Id}/members",
            new { shooterIds = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Setting_more_than_a_hundred_members_returns_a_conflict()
    {
        var competition = await CreateCompetitionAsync("Overfull Season", 2037);
        var league = await CreateLeagueAsync(competition.Id, "Division A", tier: 1);

        var shooterIds = new List<Guid>();
        for (var i = 0; i < 101; i++)
        {
            shooterIds.Add((await CreateShooterAsync($"Shooter{i}", "Overflow")).Id);
        }

        var response = await _superAdmin.PutAsJsonAsync($"/leagues/{league.Id}/members", new { shooterIds });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Setting_members_on_an_unknown_league_returns_404()
    {
        var response = await _superAdmin.PutAsJsonAsync($"/leagues/{Guid.NewGuid()}/members",
            new { shooterIds = Array.Empty<Guid>() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_closed_competition_locks_both_new_leagues_and_membership_changes()
    {
        var competition = await CreateCompetitionAsync("Closed Lock Season", 2038);
        var league = await CreateLeagueAsync(competition.Id, "Division A", tier: 1);
        var shooter = await CreateShooterAsync("Locked", "Out");

        await _superAdmin.PostAsync($"/competitions/{competition.Id}/close", content: null);

        var newLeague = await _superAdmin.PostAsJsonAsync($"/competitions/{competition.Id}/leagues",
            new { name = "Division B", tier = 2 });
        Assert.Equal(HttpStatusCode.Conflict, newLeague.StatusCode);

        var setMembers = await _superAdmin.PutAsJsonAsync($"/leagues/{league.Id}/members",
            new { shooterIds = new[] { shooter.Id } });
        Assert.Equal(HttpStatusCode.Conflict, setMembers.StatusCode);
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

    private async Task<CompetitionResponse> CreateCompetitionAsync(string name, int year)
    {
        var response = await _superAdmin.PostAsJsonAsync("/competitions",
            new { name, year, startsOn = $"{year}-01-01", endsOn = $"{year}-12-31" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompetitionResponse>())!;
    }

    private async Task<LeagueResponse> CreateLeagueAsync(Guid competitionId, string name, int tier)
    {
        var response = await _superAdmin.PostAsJsonAsync($"/competitions/{competitionId}/leagues", new { name, tier });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LeagueResponse>())!;
    }

    private async Task<ShooterResponse> CreateShooterAsync(string firstName, string lastName)
    {
        var response = await _superAdmin.PostAsJsonAsync("/shooters", new { firstName, lastName });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShooterResponse>())!;
    }
}
