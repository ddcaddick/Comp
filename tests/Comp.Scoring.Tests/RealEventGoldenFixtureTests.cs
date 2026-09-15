using System.Text.Json;

namespace Comp.Scoring.Tests;

/// <summary>
/// Reproduces a real past event's scoresheet exactly — the architecture doc's "immediate
/// next steps" ask for one, and this is it: the Wednesbury Marksmen Mini Rifle Competition
/// held 14/9/2026, supplied as a scorecard with the overall table and the five divisions'
/// points allocations. The scorecard records one final time per shooter with no separate
/// penalty breakdown, so each shooter is modelled as a single, zero-penalty run — the
/// exact scenario "a single completed run is ranked on it" already covers.
/// </summary>
public class RealEventGoldenFixtureTests
{
    private static readonly EventRules Rules = new(Version: 1, PenaltySeconds: 0.00m, TieBreak: TieBreakMode.OtherRunTime);

    [Fact]
    public void Wednesbury_marksmen_14_09_2026_reproduces_exactly_including_every_division()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "2026-09-14-wednesbury-marksmen.json");
        var fixture = JsonSerializer.Deserialize<FixtureFile>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var divisionIds = fixture.Shooters.Select(s => s.Division).Distinct()
            .ToDictionary(division => division, _ => Guid.NewGuid());

        var participantsByName = fixture.Shooters.ToDictionary(
            s => s.Name,
            s => new ParticipantInput(Guid.NewGuid(), divisionIds[s.Division], [new RunInput(1, s.TimeMs, 0, false)]));

        // The scorecard's points all start at 50 and decrement by 1, uniformly across
        // every division, so one LeagueRules value covers all five.
        var leagueRules = divisionIds.Values.ToDictionary(id => id, _ => new LeagueRules(50, 1, 0, true));

        var scores = EventScorer
            .Score(participantsByName.Values.ToList(), Rules, leagueRules)
            .Participants
            .ToDictionary(p => p.ParticipantId);

        Assert.Equal(46, fixture.Shooters.Count);

        foreach (var expected in fixture.Shooters)
        {
            var actual = scores[participantsByName[expected.Name].ParticipantId];

            Assert.Equal(ParticipantScoringStatus.Ranked, actual.Status);
            Assert.Equal(expected.TimeMs, actual.EventTimeMs);
            Assert.True(expected.OverallPosition == actual.OverallPosition,
                $"{expected.Name}: expected overall position {expected.OverallPosition}, got {actual.OverallPosition}.");
            Assert.True(expected.DivisionPosition == actual.LeaguePosition,
                $"{expected.Name}: expected division position {expected.DivisionPosition}, got {actual.LeaguePosition}.");
            Assert.True(expected.DivisionPoints == actual.LeaguePoints,
                $"{expected.Name}: expected division points {expected.DivisionPoints}, got {actual.LeaguePoints}.");
        }
    }

    private sealed record FixtureFile(string Source, List<FixtureShooter> Shooters);

    private sealed record FixtureShooter(
        string Name, int Division, int TimeMs, int OverallPosition, int DivisionPosition, int DivisionPoints);
}
