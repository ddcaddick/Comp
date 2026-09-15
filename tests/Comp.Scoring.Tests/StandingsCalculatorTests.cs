namespace Comp.Scoring.Tests;

public class StandingsCalculatorTests
{
    [Fact]
    public void Standings_are_provisional_until_more_events_than_the_drop_count_have_been_held()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 2, AbsencesCountAsZero: true);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50, 10]);

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 2).Single();

        Assert.True(standing.IsProvisional);
        Assert.Equal(60, standing.RunningTotal);
        Assert.Equal(60, standing.CountingTotal);
        Assert.Equal(0, standing.DroppedTotal);
    }

    [Fact]
    public void Standings_start_dropping_the_worst_results_exactly_one_event_after_the_drop_count()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 2, AbsencesCountAsZero: true);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50, 10, 30]);

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 3).Single();

        Assert.False(standing.IsProvisional);
        Assert.Equal(90, standing.RunningTotal);
        Assert.Equal(50, standing.CountingTotal); // worst two (10, 30) dropped, best (50) counts
        Assert.Equal(40, standing.DroppedTotal);
    }

    [Fact]
    public void A_missed_event_scores_zero_when_absences_count_as_zero()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 0, AbsencesCountAsZero: true);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50]); // attended 1 of 2 events held

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 2).Single();

        Assert.Equal(50, standing.RunningTotal); // 50 + the padded absence's 0
    }

    [Fact]
    public void A_missed_event_is_not_padded_when_absences_do_not_count_as_zero()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 1, AbsencesCountAsZero: false);
        var shooter = new ShooterEventPoints(Guid.NewGuid(), [50, 40]); // missed a 3rd event held league-wide

        var standing = StandingsCalculator.Calculate([shooter], rules, eventsHeld: 3).Single();

        // effectiveEventsHeld is this shooter's own attended count (2), not the league's
        // 3 — 2 > dropWorstCount(1), so it's already past the drop boundary for them.
        Assert.False(standing.IsProvisional);
        Assert.Equal(90, standing.RunningTotal);
        Assert.Equal(50, standing.CountingTotal);
    }

    [Fact]
    public void Ties_in_standings_share_the_position_and_skip_the_next_one()
    {
        var rules = new LeagueRules(50, 1, DropWorstCount: 0, AbsencesCountAsZero: true);
        var s1 = new ShooterEventPoints(Guid.NewGuid(), [50]);
        var s2 = new ShooterEventPoints(Guid.NewGuid(), [50]);
        var s3 = new ShooterEventPoints(Guid.NewGuid(), [40]);

        var standings = StandingsCalculator.Calculate([s1, s2, s3], rules, eventsHeld: 1)
            .ToDictionary(s => s.ShooterId);

        Assert.Equal(1, standings[s1.ShooterId].Position);
        Assert.Equal(1, standings[s2.ShooterId].Position);
        Assert.Equal(3, standings[s3.ShooterId].Position);
    }
}
