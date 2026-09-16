using Comp.Application.Abstractions;
using Comp.Contracts.Leagues;
using Comp.Domain;
using Comp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Leagues;

public class LeagueService(CompDbContext dbContext) : ILeagueService
{
    // D4 in the architecture doc originally capped this at 20 specifically so the default
    // scoring (50 points for 1st, -1 per position) never reached zero. Raised to 100 per
    // explicit user direction -- the default scoring alone no longer guarantees that above
    // position 50 (it goes negative), so a league expecting more than ~50 counted finishers
    // needs its own PointsForFirst/PointsDecrement set accordingly; nothing enforces that
    // automatically.
    private const int MaxMembersPerLeague = 100;

    public async Task<LeagueResult> CreateAsync(Guid competitionId, CreateLeagueRequest request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions.FindAsync([competitionId], cancellationToken);
        if (competition is null)
        {
            return new LeagueResult.NotFound();
        }

        if (competition.Status == CompetitionStatus.Closed)
        {
            return new LeagueResult.Conflict("Cannot add a league to a closed competition.");
        }

        // Service-level check ahead of the unique(competition_id, tier) and
        // unique(competition_id, name) indexes.
        var duplicate = await dbContext.Leagues.AnyAsync(
            l => l.CompetitionId == competitionId && (l.Tier == request.Tier || l.Name == request.Name),
            cancellationToken);
        if (duplicate)
        {
            return new LeagueResult.Conflict("A league with that name or tier already exists in this competition.");
        }

        var league = new League
        {
            CompetitionId = competitionId,
            Name = request.Name,
            Tier = request.Tier,
            PointsForFirst = request.PointsForFirst ?? 50,
            PointsDecrement = request.PointsDecrement ?? 1,
            DropWorstCount = request.DropWorstCount ?? 0,
            AbsencesCountAsZero = request.AbsencesCountAsZero ?? true
        };
        dbContext.Leagues.Add(league);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LeagueResult.Success(ToLeagueResponse(league));
    }

    public async Task<IReadOnlyList<LeagueResponse>?> ListForCompetitionAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions.AnyAsync(c => c.Id == competitionId, cancellationToken);
        if (!competitionExists)
        {
            return null;
        }

        var leagues = await dbContext.Leagues
            .Where(l => l.CompetitionId == competitionId)
            .OrderBy(l => l.Tier)
            .ToListAsync(cancellationToken);

        return leagues.Select(ToLeagueResponse).ToList();
    }

    public async Task<LeagueMembersResult> GetMembersAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        var leagueExists = await dbContext.Leagues.AnyAsync(l => l.Id == leagueId, cancellationToken);
        if (!leagueExists)
        {
            return new LeagueMembersResult.NotFound();
        }

        var members = await LoadMembersAsync(leagueId, cancellationToken);
        return new LeagueMembersResult.Success(members);
    }

    public async Task<LeagueMembersResult> SetMembersAsync(
        Guid leagueId, SetLeagueMembersRequest request, CancellationToken cancellationToken)
    {
        var league = await dbContext.Leagues.FindAsync([leagueId], cancellationToken);
        if (league is null)
        {
            return new LeagueMembersResult.NotFound();
        }

        var competition = await dbContext.Competitions.FindAsync([league.CompetitionId], cancellationToken);
        if (competition!.Status == CompetitionStatus.Closed)
        {
            return new LeagueMembersResult.Conflict("Cannot change membership in a closed competition.");
        }

        var requestedIds = request.ShooterIds.Distinct().ToList();
        if (requestedIds.Count > MaxMembersPerLeague)
        {
            return new LeagueMembersResult.Conflict($"A league cannot have more than {MaxMembersPerLeague} members.");
        }

        var knownShooterCount = await dbContext.Shooters.CountAsync(s => requestedIds.Contains(s.Id), cancellationToken);
        if (knownShooterCount != requestedIds.Count)
        {
            return new LeagueMembersResult.Conflict("One or more shooter ids do not exist.");
        }

        var requestedSet = requestedIds.ToHashSet();

        // Members currently in this league but dropped from the new list are removed
        // outright — a league membership is a current-status record, not a permanent one
        // like a run or a result (see the architecture doc's section D on membership
        // corrections).
        var currentMembers = await dbContext.LeagueMemberships
            .Where(m => m.LeagueId == leagueId)
            .ToListAsync(cancellationToken);
        var toRemove = currentMembers.Where(m => !requestedSet.Contains(m.ShooterId)).ToList();
        dbContext.LeagueMemberships.RemoveRange(toRemove);

        // A shooter already a member of a different league in this competition is moved
        // rather than rejected — see ILeagueService.SetMembersAsync's remarks.
        var existingInCompetition = await dbContext.LeagueMemberships
            .Where(m => m.CompetitionId == league.CompetitionId && requestedSet.Contains(m.ShooterId))
            .ToDictionaryAsync(m => m.ShooterId, cancellationToken);

        foreach (var shooterId in requestedIds)
        {
            if (existingInCompetition.TryGetValue(shooterId, out var existing))
            {
                existing.LeagueId = leagueId;
            }
            else
            {
                dbContext.LeagueMemberships.Add(new LeagueMembership
                {
                    CompetitionId = league.CompetitionId,
                    LeagueId = leagueId,
                    ShooterId = shooterId
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var members = await LoadMembersAsync(leagueId, cancellationToken);
        return new LeagueMembersResult.Success(members);
    }

    private async Task<IReadOnlyList<LeagueMemberResponse>> LoadMembersAsync(Guid leagueId, CancellationToken cancellationToken) =>
        await dbContext.LeagueMemberships
            .Where(m => m.LeagueId == leagueId)
            .Join(dbContext.Shooters, m => m.ShooterId, s => s.Id, (m, s) => new { m, s })
            // Order on the raw columns before projecting — EF Core can't translate an
            // ORDER BY on a property of an already-constructed record.
            .OrderBy(x => x.s.LastName).ThenBy(x => x.s.FirstName)
            .Select(x => new LeagueMemberResponse(x.s.Id, x.s.FirstName, x.s.LastName, x.m.AssignedAt))
            .ToListAsync(cancellationToken);

    private static LeagueResponse ToLeagueResponse(League league) => new(
        league.Id,
        league.CompetitionId,
        league.Name,
        league.Tier,
        league.PointsForFirst,
        league.PointsDecrement,
        league.DropWorstCount,
        league.AbsencesCountAsZero);
}
