using Comp.Application.Abstractions;
using Comp.Contracts.Events;
using Comp.Contracts.Leagues;
using Comp.Domain;
using Comp.Domain.Enums;
using Comp.Scoring;
using Microsoft.EntityFrameworkCore;

namespace Comp.Infrastructure.Events;

/// <summary>
/// The only place <c>Comp.Scoring</c> is called from the API side. Every query here loads
/// flat lists and joins them in memory (the pattern already established by
/// <c>RunnerService</c>/<c>RunService</c>) rather than composing deep LINQ-to-SQL joins —
/// EF Core's translator has already tripped on ordering-after-projecting once in this
/// codebase (see <c>LeagueService.LoadMembersAsync</c>'s history); this sidesteps it
/// entirely rather than re-litigating which shapes it can translate.
/// </summary>
public class ResultsService(CompDbContext dbContext) : IResultsService
{
    public async Task<EventResultsResponse?> GetEventResultsAsync(
        Guid eventId, Guid? leagueId, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.FindAsync([eventId], cancellationToken);
        if (@event is null)
        {
            return null;
        }

        var isFinal = @event.Status == EventStatus.Finalised;
        var rows = isFinal
            ? await LoadFrozenAsync(@event, cancellationToken)
            : await ComputeLiveAsync(@event, cancellationToken);

        var filtered = leagueId is null
            ? rows.OrderBy(r => r.OverallPosition ?? int.MaxValue).ToList()
            : rows.Where(r => r.LeagueId == leagueId).OrderBy(r => r.LeaguePosition ?? int.MaxValue).ToList();

        return new EventResultsResponse(eventId, @event.Status.ToString(), isFinal, filtered);
    }

    public async Task RecalculateAndPersistAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var @event = await dbContext.Events.SingleAsync(e => e.Id == eventId, cancellationToken);
        var context = await LoadScoringContextAsync(@event, cancellationToken);
        var scored = Score(@event, context);

        var existing = await dbContext.EventResults.Where(r => r.EventId == eventId).ToListAsync(cancellationToken);
        dbContext.EventResults.RemoveRange(existing);

        foreach (var score in scored.Participants)
        {
            Guid? bestRunId = null;
            if (score.BestRunNumber is not null &&
                context.RunsByParticipant.TryGetValue(score.ParticipantId, out var runs))
            {
                bestRunId = runs.SingleOrDefault(r => r.RunNumber == score.BestRunNumber)?.Id;
            }

            dbContext.EventResults.Add(new EventResult
            {
                EventId = eventId,
                EventParticipantId = score.ParticipantId,
                LeagueId = score.LeagueId,
                BestRunId = bestRunId,
                EventTimeMs = score.EventTimeMs,
                Status = score.Status == ParticipantScoringStatus.Ranked ? EventResultStatus.Ranked : EventResultStatus.Dnf,
                OverallPosition = score.OverallPosition,
                LeaguePosition = score.LeaguePosition,
                LeaguePoints = score.LeaguePoints,
                RulesVersion = @event.ScoringRulesVersion
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<LeagueStandingsResult> GetLeagueStandingsAsync(Guid leagueId, CancellationToken cancellationToken)
    {
        var league = await dbContext.Leagues.FindAsync([leagueId], cancellationToken);
        if (league is null)
        {
            return new LeagueStandingsResult.NotFound();
        }

        // Only a finalised, counts-for-standings event has event_results rows at all — no
        // extra filter needed beyond "belongs to this competition" once joined to them.
        var qualifyingEvents = await dbContext.Events
            .Where(e => e.CompetitionId == league.CompetitionId && e.Status == EventStatus.Finalised && e.CountsForStandings)
            .OrderBy(e => e.EventNumber)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        var eventOrder = qualifyingEvents.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        var eventsHeld = qualifyingEvents.Count;

        var results = await dbContext.EventResults
            .Where(r => r.LeagueId == leagueId && qualifyingEvents.Contains(r.EventId))
            .ToListAsync(cancellationToken);

        var participantIds = results.Select(r => r.EventParticipantId).Distinct().ToList();
        var shooterIdByParticipant = await dbContext.EventParticipants
            .Where(p => participantIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ShooterId, cancellationToken);

        // A shooter's EventParticipant row is a different id every event they enter, so
        // points are grouped by shooter, not by participant — that's the whole point of
        // resolving through shooterIdByParticipant here rather than grouping results directly.
        var points = results
            .GroupBy(r => shooterIdByParticipant[r.EventParticipantId])
            .Select(g => new ShooterEventPoints(
                g.Key,
                g.OrderBy(r => eventOrder[r.EventId]).Select(r => r.LeaguePoints).ToList()))
            .ToList();

        var rules = new LeagueRules(league.PointsForFirst, league.PointsDecrement, league.DropWorstCount, league.AbsencesCountAsZero);
        var standings = StandingsCalculator.Calculate(points, rules, eventsHeld);

        var shooterIds = standings.Select(s => s.ShooterId).ToList();
        var shooters = await dbContext.Shooters
            .Where(s => shooterIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var response = standings.Select(s => new LeagueStandingResponse(
            s.ShooterId,
            shooters[s.ShooterId].FirstName,
            shooters[s.ShooterId].LastName,
            s.Position,
            s.RunningTotal,
            s.CountingTotal,
            s.DroppedTotal,
            s.IsProvisional)).ToList();

        return new LeagueStandingsResult.Success(new LeagueStandingsResponse(league.Id, league.Name, eventsHeld, response));
    }

    private async Task<List<EventParticipantResultResponse>> ComputeLiveAsync(Event @event, CancellationToken cancellationToken)
    {
        var context = await LoadScoringContextAsync(@event, cancellationToken);
        var scored = Score(@event, context);

        return scored.Participants.Select(score =>
        {
            var (shooterId, firstName, lastName) = context.ParticipantInfo[score.ParticipantId];
            var leagueName = score.LeagueId is { } leagueId ? context.LeagueNames.GetValueOrDefault(leagueId) : null;
            return new EventParticipantResultResponse(
                score.ParticipantId, shooterId, firstName, lastName, score.LeagueId, leagueName,
                score.Status.ToString(), score.EventTimeMs, score.BestRunNumber,
                score.OverallPosition, score.LeaguePosition, score.LeaguePoints);
        }).ToList();
    }

    private async Task<List<EventParticipantResultResponse>> LoadFrozenAsync(Event @event, CancellationToken cancellationToken)
    {
        var results = await dbContext.EventResults.Where(r => r.EventId == @event.Id).ToListAsync(cancellationToken);
        var participantIds = results.Select(r => r.EventParticipantId).ToList();

        var participants = await dbContext.EventParticipants
            .Where(p => participantIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var shooterIds = participants.Select(p => p.ShooterId).Distinct().ToList();
        var shooters = await dbContext.Shooters.Where(s => shooterIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var participantsById = participants.ToDictionary(p => p.Id);

        var leagueIds = results.Where(r => r.LeagueId is not null).Select(r => r.LeagueId!.Value).Distinct().ToList();
        var leagueNames = await dbContext.Leagues.Where(l => leagueIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var runIds = results.Where(r => r.BestRunId is not null).Select(r => r.BestRunId!.Value).ToList();
        var runNumbersById = await dbContext.Runs.Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.RunNumber, cancellationToken);

        return results.Select(r =>
        {
            var participant = participantsById[r.EventParticipantId];
            var shooter = shooters[participant.ShooterId];
            var leagueName = r.LeagueId is { } leagueId ? leagueNames.GetValueOrDefault(leagueId) : null;
            var bestRunNumber = r.BestRunId is { } runId ? runNumbersById.GetValueOrDefault(runId) : (int?)null;
            return new EventParticipantResultResponse(
                r.EventParticipantId, participant.ShooterId, shooter.FirstName, shooter.LastName,
                r.LeagueId, leagueName, r.Status.ToString(), r.EventTimeMs, bestRunNumber,
                r.OverallPosition, r.LeaguePosition, r.LeaguePoints);
        }).ToList();
    }

    private async Task<ScoringContext> LoadScoringContextAsync(Event @event, CancellationToken cancellationToken)
    {
        var participants = await dbContext.EventParticipants
            .Where(p => p.EventId == @event.Id)
            .Join(dbContext.Shooters, p => p.ShooterId, s => s.Id, (p, s) => new { p, s })
            .ToListAsync(cancellationToken);

        var participantIds = participants.Select(x => x.p.Id).ToList();
        var runsByParticipant = (await dbContext.Runs
                .Where(r => participantIds.Contains(r.EventParticipantId))
                .ToListAsync(cancellationToken))
            .ToLookup(r => r.EventParticipantId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var leagueIds = participants.Where(x => x.p.LeagueId is not null).Select(x => x.p.LeagueId!.Value).Distinct().ToList();
        var leagues = await dbContext.Leagues.Where(l => leagueIds.Contains(l.Id)).ToListAsync(cancellationToken);

        var participantInputs = participants.Select(x => new ParticipantInput(
            x.p.Id,
            x.p.LeagueId,
            runsByParticipant.GetValueOrDefault(x.p.Id, [])
                .Select(r => new RunInput(r.RunNumber, r.RawTimeMs, r.PenaltyCount, r.IsDnf))
                .ToList())).ToList();

        var leagueRules = leagues.ToDictionary(
            l => l.Id,
            l => new LeagueRules(l.PointsForFirst, l.PointsDecrement, l.DropWorstCount, l.AbsencesCountAsZero));

        var participantInfo = participants.ToDictionary(x => x.p.Id, x => (x.s.Id, x.s.FirstName, x.s.LastName));
        var leagueNames = leagues.ToDictionary(l => l.Id, l => l.Name);

        return new ScoringContext(participantInputs, leagueRules, runsByParticipant, participantInfo, leagueNames);
    }

    private static EventScoringResult Score(Event @event, ScoringContext context) =>
        EventScorer.Score(
            context.Participants,
            new EventRules(@event.ScoringRulesVersion, @event.PenaltySeconds, TieBreakMode.OtherRunTime),
            context.LeagueRules);

    private sealed record ScoringContext(
        IReadOnlyList<ParticipantInput> Participants,
        IReadOnlyDictionary<Guid, LeagueRules> LeagueRules,
        IReadOnlyDictionary<Guid, List<Run>> RunsByParticipant,
        IReadOnlyDictionary<Guid, (Guid ShooterId, string FirstName, string LastName)> ParticipantInfo,
        IReadOnlyDictionary<Guid, string> LeagueNames);
}
