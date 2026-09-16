using FsCheck;
using FsCheck.Xunit;

namespace Comp.Scoring.Tests;

public class StandingsCalculatorPropertyTests
{
    [Property]
    public bool Counting_total_never_exceeds_running_total(
        PositiveInt dropWorstCandidate, NonEmptyArray<NonNegativeInt> pointsPerEvent, PositiveInt extraEventsHeld)
    {
        // Points are constrained to a realistic 0-50 range: "no points floor" (D4) means
        // this library never clamps a negative points value, but real league points are
        // never negative in practice (leagues cap at 20 shooters elsewhere), which is the
        // condition this invariant actually assumes.
        var points = pointsPerEvent.Get.Select(p => p.Get % 51).ToList();
        var eventsHeld = points.Count + extraEventsHeld.Get % 5;
        var dropWorst = dropWorstCandidate.Get % 10;

        var rules = new LeagueRules(50, 1, dropWorst, AbsencesCountAsZero: true);
        var shooterPoints = new ShooterEventPoints(Guid.NewGuid(), points, EventsMissed: 0);

        var standing = StandingsCalculator.Calculate([shooterPoints], rules, eventsHeld).Single();

        return standing.CountingTotal <= standing.RunningTotal;
    }

    [Property]
    public bool Standings_are_stable_under_input_reordering(
        NonEmptyArray<NonNegativeInt> points1, NonEmptyArray<NonNegativeInt> points2, NonEmptyArray<NonNegativeInt> points3)
    {
        const int eventsHeld = 5;
        var rules = new LeagueRules(50, 1, DropWorstCount: 1, AbsencesCountAsZero: true);

        var shooters = new[]
        {
            new ShooterEventPoints(Guid.NewGuid(), points1.Get.Select(p => p.Get % 51).Take(eventsHeld).ToList(), EventsMissed: 0),
            new ShooterEventPoints(Guid.NewGuid(), points2.Get.Select(p => p.Get % 51).Take(eventsHeld).ToList(), EventsMissed: 0),
            new ShooterEventPoints(Guid.NewGuid(), points3.Get.Select(p => p.Get % 51).Take(eventsHeld).ToList(), EventsMissed: 0),
        };

        var original = StandingsCalculator.Calculate(shooters, rules, eventsHeld).ToDictionary(s => s.ShooterId);
        var reversed = StandingsCalculator.Calculate(shooters.Reverse().ToArray(), rules, eventsHeld)
            .ToDictionary(s => s.ShooterId);

        return shooters.All(s =>
            original[s.ShooterId].RunningTotal == reversed[s.ShooterId].RunningTotal &&
            original[s.ShooterId].CountingTotal == reversed[s.ShooterId].CountingTotal &&
            original[s.ShooterId].Position == reversed[s.ShooterId].Position);
    }
}
