namespace Comp.Scoring.Tests;

public class StandingsCalculatorTests
{
    [Fact]
    public void Standings_are_provisional_until_more_events_than_the_drop_count_have_been_held()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 2, AbsencesCountAsZero: true);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50, 10], EventsMissed: 0);

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 2).Single();

        Assert.True(standing.IsProvisional);
        Assert.Equal(60, standing.RunningTotal);
        Assert.Equal(60, standing.CountingTotal);
        Assert.Equal(0, standing.MissedEvents);
    }

    [Fact]
    public void Standings_start_dropping_the_worst_results_exactly_one_event_after_the_drop_count()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 2, AbsencesCountAsZero: true);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50, 10, 30], EventsMissed: 0);

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 3).Single();

        Assert.False(standing.IsProvisional);
        Assert.Equal(90, standing.RunningTotal);
        Assert.Equal(50, standing.CountingTotal); // worst two (10, 30) dropped, best (50) counts
    }

    [Fact]
    public void Standings_stay_provisional_through_the_drop_count_and_start_counting_the_event_after()
    {
        // The user's own worked example: dropWorstCount = 4 means the league doesn't start
        // populating real totals until the 5th finalised event.
        var rules = new LeagueRules(50, 1, DropWorstCount: 4, AbsencesCountAsZero: true);
        var afterFour = new ShooterEventPoints(Guid.NewGuid(), [50, 40, 30, 20], EventsMissed: 0);
        var afterFive = new ShooterEventPoints(afterFour.ShooterId, [50, 40, 30, 20, 10], EventsMissed: 0);

        var stillProvisional = StandingsCalculator.Calculate([afterFour], rules, eventsHeld: 4).Single();
        Assert.True(stillProvisional.IsProvisional);

        var nowCounting = StandingsCalculator.Calculate([afterFive], rules, eventsHeld: 5).Single();
        Assert.False(nowCounting.IsProvisional);
        Assert.Equal(150, nowCounting.RunningTotal);
        Assert.Equal(50, nowCounting.CountingTotal); // worst four (40, 30, 20, 10) dropped, best (50) counts
    }

    [Fact]
    public void A_missed_event_scores_zero_when_absences_count_as_zero()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 0, AbsencesCountAsZero: true);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50], EventsMissed: 1); // attended 1 of 2 events held

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 2).Single();

        Assert.Equal(50, standing.RunningTotal); // 50 + the padded absence's 0
        Assert.Equal(1, standing.MissedEvents);
    }

    [Fact]
    public void A_missed_event_is_not_padded_when_absences_do_not_count_as_zero()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 1, AbsencesCountAsZero: false);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50, 40], EventsMissed: 1); // missed a 3rd event held league-wide

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 3).Single();

        // effectiveEventsHeld is this shooter's own attended count (2), not the league's
        // 3 — 2 > dropWorstCount(1), so it's already past the drop boundary for them. Note
        // MissedEvents (a factual attendance count) is unaffected by AbsencesCountAsZero:
        // they still missed one event regardless of how that missed event scores.
        Assert.False(standing.IsProvisional);
        Assert.Equal(90, standing.RunningTotal);
        Assert.Equal(50, standing.CountingTotal);
        Assert.Equal(1, standing.MissedEvents);
    }

    [Fact]
    public void Ties_in_standings_share_the_position_and_skip_the_next_one()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 0, AbsencesCountAsZero: true);
        var s1 = new ShooterEventPoints(Guid.NewGuid(), [50], EventsMissed: 0);
        var s2 = new ShooterEventPoints(Guid.NewGuid(), [50], EventsMissed: 0);
        var s3 = new ShooterEventPoints(Guid.NewGuid(), [40], EventsMissed: 0);

        var standings = StandingsCalculator.Calculate([s1, s2, s3], rules, eventsHeld: 1)
            .ToDictionary(s => s.ShooterId);

        Assert.Equal(1, standings[s1.ShooterId].Position);
        Assert.Equal(1, standings[s2.ShooterId].Position);
        Assert.Equal(3, standings[s3.ShooterId].Position);
    }
}
