namespace Comp.Scoring;

/// <summary>
/// The only place an event's result is ever computed. See the architecture doc's
/// section I for the full algorithm; this implementation follows it exactly, with one
/// necessary extension: scoring points for a participant's league needs that league's
/// <see cref="LeagueRules"/> (<c>PointsForFirst</c>/<c>PointsDecrement</c>), and a single
/// event scores for every league in its competition simultaneously, each potentially
/// configured differently — so <see cref="Score"/> takes a rules lookup keyed by league,
/// rather than the single <c>EventRules</c> the doc's illustrative signature shows.
/// </summary>
public static class EventScorer
{
    public static EventScoringResult Score(
        IReadOnlyList<ParticipantInput> participants,
        EventRules eventRules,
        IReadOnlyDictionary<Guid, LeagueRules> leagueRules)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(leagueRules);

        var working = participants.Select(p => ComputeAdjustedTimes(p, eventRules)).ToList();
        var ranked = working.Where(w => w.EventTimeMs is not null).ToList();

        var overallOrder = OrderForRanking(ranked);
        var overallPositions = AssignPositions(overallOrder);
        var overallPositionById = Zip(overallOrder, overallPositions);

        var leaguePositionById = new Dictionary<Guid, int>();
        var leaguePointsById = new Dictionary<Guid, int>();

        foreach (var group in ranked.Where(w => w.LeagueId.HasValue).GroupBy(w => w.LeagueId!.Value))
        {
            if (!leagueRules.TryGetValue(group.Key, out var rulesForLeague))
            {
                throw new ArgumentException(
                    $"No LeagueRules supplied for league {group.Key}, but a participant snapshotted it.",
                    nameof(leagueRules));
            }

            var leagueOrder = OrderForRanking(group.ToList());
            var leaguePositions = AssignPositions(leagueOrder);

            for (var i = 0; i < leagueOrder.Count; i++)
            {
                var participantId = leagueOrder[i].ParticipantId;
                var position = leaguePositions[i];
                leaguePositionById[participantId] = position;
                // No points floor (architecture doc decision D4) — leagues cap at 20
                // shooters elsewhere, so this is never negative in practice, but this
                // library doesn't clamp it itself.
                leaguePointsById[participantId] =
                    rulesForLeague.PointsForFirst - (position - 1) * rulesForLeague.PointsDecrement;
            }
        }

        var scoresById = working.ToDictionary(w => w.ParticipantId, w => ToParticipantScore(
            w, overallPositionById, leaguePositionById, leaguePointsById));

        return new EventScoringResult(participants.Select(p => scoresById[p.ParticipantId]).ToList());
    }

    private static ParticipantScore ToParticipantScore(
        ScoredParticipant w,
        IReadOnlyDictionary<Guid, int> overallPositionById,
        IReadOnlyDictionary<Guid, int> leaguePositionById,
        IReadOnlyDictionary<Guid, int> leaguePointsById)
    {
        if (w.EventTimeMs is null)
        {
            // Either way, listed in the results table, unranked, zero league points —
            // never a position anywhere, overall or league. Distinguished only for
            // display: NotRun means nothing has been recorded for them yet; Dnf means
            // at least one run was actually marked DNF on the entry screen.
            var status = w.HasNoRuns ? ParticipantScoringStatus.NotRun : ParticipantScoringStatus.Dnf;
            return new ParticipantScore(w.ParticipantId, w.LeagueId, status, null, null, null, null, 0);
        }

        return new ParticipantScore(
            w.ParticipantId,
            w.LeagueId,
            ParticipantScoringStatus.Ranked,
            w.EventTimeMs,
            w.BestRunNumber,
            overallPositionById[w.ParticipantId],
            leaguePositionById.TryGetValue(w.ParticipantId, out var leaguePosition) ? leaguePosition : null,
            leaguePointsById.GetValueOrDefault(w.ParticipantId));
    }

    private static ScoredParticipant ComputeAdjustedTimes(ParticipantInput participant, EventRules rules)
    {
        var adjustedByRun = participant.Runs
            .Select(r => (r.RunNumber, Adjusted: ComputeAdjusted(r, rules)))
            .Where(r => r.Adjusted is not null)
            .OrderBy(r => r.Adjusted!.Value)
            .ToList();

        if (adjustedByRun.Count == 0)
        {
            // Both DNF, or an empty run list — either way, no valid run to rank on.
            // HasNoRuns (literally zero runs recorded, as opposed to one or more
            // explicitly marked DNF) is what tells ToParticipantScore which of those two
            // this actually was.
            return new ScoredParticipant(
                participant.ParticipantId, participant.LeagueId, null, null, null, participant.Runs.Count == 0);
        }

        var best = adjustedByRun[0];
        // The "other" run for the D3 tie-break is the runner-up valid time, if there is
        // one — this generalises the doc's two-run example to any number of runs.
        int? otherRunAdjusted = adjustedByRun.Count > 1 ? adjustedByRun[1].Adjusted : null;

        return new ScoredParticipant(
            participant.ParticipantId, participant.LeagueId, best.Adjusted, otherRunAdjusted, best.RunNumber, false);
    }

    private static int? ComputeAdjusted(RunInput run, EventRules rules)
    {
        if (run.IsDnf)
        {
            return null;
        }

        if (run.RawTimeMs is null)
        {
            throw new ArgumentException("A run that is not DNF must have a raw time.", nameof(run));
        }

        if (run.PenaltyCount < 0)
        {
            throw new ArgumentException("Penalty count cannot be negative.", nameof(run));
        }

        var penaltyMs = (int)(run.PenaltyCount * rules.PenaltySeconds * 1000m);
        return run.RawTimeMs.Value + penaltyMs;
    }

    /// <summary>
    /// Fastest eventTime first; on a tie, whichever has a faster (or any, if the other
    /// doesn't) valid other run — implementing D3's tie-break as a single sort key rather
    /// than a separate comparison pass.
    /// </summary>
    private static List<ScoredParticipant> OrderForRanking(List<ScoredParticipant> ranked) =>
        ranked
            .OrderBy(p => p.EventTimeMs)
            .ThenByDescending(p => p.OtherRunAdjustedMs.HasValue)
            .ThenBy(p => p.OtherRunAdjustedMs ?? int.MaxValue)
            .ToList();

    /// <summary>
    /// Standard competition ranking (1, 2, 2, 4 — not 1, 2, 2, 3): a tie with the
    /// previous entry on every tie-break key shares its position; the position number
    /// itself still reflects how many entries came before, so the following distinct
    /// entry's position is correctly skipped ahead.
    /// </summary>
    private static int[] AssignPositions(IReadOnlyList<ScoredParticipant> ordered)
    {
        var positions = new int[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            positions[i] = i > 0 && IsTiedWith(ordered[i], ordered[i - 1]) ? positions[i - 1] : i + 1;
        }

        return positions;
    }

    private static bool IsTiedWith(ScoredParticipant a, ScoredParticipant b) =>
        a.EventTimeMs == b.EventTimeMs && a.OtherRunAdjustedMs == b.OtherRunAdjustedMs;

    private static Dictionary<Guid, int> Zip(IReadOnlyList<ScoredParticipant> entries, int[] positions)
    {
        var result = new Dictionary<Guid, int>();
        for (var i = 0; i < entries.Count; i++)
        {
            result[entries[i].ParticipantId] = positions[i];
        }

        return result;
    }

    private readonly record struct ScoredParticipant(
        Guid ParticipantId,
        Guid? LeagueId,
        int? EventTimeMs,
        int? OtherRunAdjustedMs,
        int? BestRunNumber,
        bool HasNoRuns);
}
