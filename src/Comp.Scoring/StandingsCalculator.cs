namespace Comp.Scoring;

/// <summary>The only place league standings are ever computed — see the architecture doc's section I.</summary>
public static class StandingsCalculator
{
    public static IReadOnlyList<LeagueStanding> Calculate(
        IReadOnlyList<ShooterEventPoints> points, LeagueRules rules, int eventsHeld)
    {
        ArgumentNullException.ThrowIfNull(points);

        var computed = points.Select(p => ComputeOne(p, rules, eventsHeld)).ToList();

        // Descending by counting total. No secondary tie-break is specified for
        // standings (unlike event-night ties, which have D3) — a genuine tie in the
        // table is left as a shared position.
        var ordered = computed.OrderByDescending(c => c.CountingTotal).ToList();

        var result = new List<LeagueStanding>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var position = i > 0 && ordered[i].CountingTotal == ordered[i - 1].CountingTotal
                ? result[i - 1].Position
                : i + 1;
            result.Add(ordered[i] with { Position = position });
        }

        return result;
    }

    private static LeagueStanding ComputeOne(ShooterEventPoints shooterPoints, LeagueRules rules, int eventsHeld)
    {
        var entries = shooterPoints.EventPoints.ToList();

        // Absences count as zero: pad up to the number of events actually held so far.
        // When they don't, a missed event simply isn't in the list at all, and the drop
        // rule below applies only among the events this shooter actually attended.
        var effectiveEventsHeld = eventsHeld;
        if (rules.AbsencesCountAsZero)
        {
            var missing = eventsHeld - entries.Count;
            if (missing > 0)
            {
                entries.AddRange(Enumerable.Repeat(0, missing));
            }
        }
        else
        {
            effectiveEventsHeld = entries.Count;
        }

        var runningTotal = entries.Sum();

        // D7: the drop rule applies continuously — with dropWorstCount = 5, standings
        // begin counting only once event 6 has been held, and always exclude the worst
        // five to date from then on.
        int countingTotal;
        bool isProvisional;
        if (effectiveEventsHeld > rules.DropWorstCount)
        {
            var dropped = entries.OrderBy(x => x).Take(rules.DropWorstCount).Sum();
            countingTotal = runningTotal - dropped;
            isProvisional = false;
        }
        else
        {
            countingTotal = runningTotal;
            isProvisional = true;
        }

        return new LeagueStanding(
            shooterPoints.ShooterId, runningTotal, countingTotal, shooterPoints.EventsMissed, isProvisional, 0);
    }
}
