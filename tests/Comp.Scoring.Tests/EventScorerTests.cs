namespace Comp.Scoring.Tests;

public class EventScorerTests
{
    private static readonly EventRules Rules = new(Version: 1, PenaltySeconds: 5.00m, TieBreak: TieBreakMode.OtherRunTime);

    [Fact]
    public void Both_dnf_are_listed_unranked_with_zero_points()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var participant = new ParticipantInput(Guid.NewGuid(), leagueId,
        [
            new RunInput(1, null, 0, true),
            new RunInput(2, null, 0, true),
        ]);

        var score = EventScorer.Score([participant], Rules, leagueRules).Participants.Single();

        Assert.Equal(ParticipantScoringStatus.Dnf, score.Status);
        Assert.Null(score.EventTimeMs);
        Assert.Null(score.BestRunNumber);
        Assert.Null(score.OverallPosition);
        Assert.Null(score.LeaguePosition);
        Assert.Equal(0, score.LeaguePoints);
    }

    [Fact]
    public void No_runs_recorded_at_all_is_not_run_rather_than_dnf()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var neverUp = new ParticipantInput(Guid.NewGuid(), leagueId, []);

        var score = EventScorer.Score([neverUp], Rules, leagueRules).Participants.Single();

        Assert.Equal(ParticipantScoringStatus.NotRun, score.Status);
        Assert.Null(score.EventTimeMs);
        Assert.Null(score.OverallPosition);
        Assert.Null(score.LeaguePosition);
        Assert.Equal(0, score.LeaguePoints);
    }

    [Fact]
    public void A_single_recorded_dnf_with_the_other_run_still_outstanding_is_dnf_not_not_run()
    {
        // Only run 1 has been entered so far (run 2 hasn't happened yet), and it was
        // marked DNF -- this participant has genuinely been up, so they're Dnf, not
        // NotRun, even though a valid time still isn't in yet either.
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var participant = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, null, 0, true)]);

        var score = EventScorer.Score([participant], Rules, leagueRules).Participants.Single();

        Assert.Equal(ParticipantScoringStatus.Dnf, score.Status);
    }

    [Fact]
    public void One_dnf_plus_one_valid_run_the_valid_run_stands()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var participant = new ParticipantInput(Guid.NewGuid(), leagueId,
        [
            new RunInput(1, 100_000, 0, false),
            new RunInput(2, null, 0, true),
        ]);

        var score = EventScorer.Score([participant], Rules, leagueRules).Participants.Single();

        Assert.Equal(ParticipantScoringStatus.Ranked, score.Status);
        Assert.Equal(100_000, score.EventTimeMs);
        Assert.Equal(1, score.BestRunNumber);
        Assert.Equal(1, score.OverallPosition);
        Assert.Equal(1, score.LeaguePosition);
        Assert.Equal(50, score.LeaguePoints);
    }

    [Fact]
    public void A_single_completed_run_with_no_second_run_at_all_is_ranked_on_it()
    {
        var participant = new ParticipantInput(Guid.NewGuid(), null, [new RunInput(1, 90_000, 2, false)]);

        var score = EventScorer.Score([participant], Rules, new Dictionary<Guid, LeagueRules>()).Participants.Single();

        Assert.Equal(ParticipantScoringStatus.Ranked, score.Status);
        Assert.Equal(90_000 + 2 * 5_000, score.EventTimeMs); // 100_000
        Assert.Equal(1, score.OverallPosition);
        Assert.Null(score.LeaguePosition); // never assigned to a league
        Assert.Equal(0, score.LeaguePoints);
    }

    [Fact]
    public void Exact_ties_are_broken_by_the_faster_other_run()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var a = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 100_000, 0, false), new RunInput(2, 110_000, 0, false)]);
        var b = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 100_000, 0, false), new RunInput(2, 105_000, 0, false)]);

        var scores = EventScorer.Score([a, b], Rules, leagueRules).Participants.ToDictionary(p => p.ParticipantId);

        // Both ran the same best time (100_000); b's other run (105_000) is faster than
        // a's (110_000), so b wins the tie.
        Assert.Equal(1, scores[b.ParticipantId].LeaguePosition);
        Assert.Equal(50, scores[b.ParticipantId].LeaguePoints);
        Assert.Equal(2, scores[a.ParticipantId].LeaguePosition);
        Assert.Equal(49, scores[a.ParticipantId].LeaguePoints);
    }

    [Fact]
    public void Two_valid_runs_beats_a_single_valid_run_on_an_exact_tie()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var twoRuns = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 100_000, 0, false), new RunInput(2, 120_000, 0, false)]);
        var oneRun = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, 100_000, 0, false)]);

        var scores = EventScorer.Score([twoRuns, oneRun], Rules, leagueRules)
            .Participants.ToDictionary(p => p.ParticipantId);

        Assert.Equal(1, scores[twoRuns.ParticipantId].LeaguePosition);
        Assert.Equal(2, scores[oneRun.ParticipantId].LeaguePosition);
    }

    [Fact]
    public void A_true_tie_shares_the_position_takes_the_higher_points_and_skips_the_next_position()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };
        var a = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, 100_000, 0, false)]);
        var b = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, 100_000, 0, false)]);
        var c = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, 110_000, 0, false)]);

        var scores = EventScorer.Score([a, b, c], Rules, leagueRules).Participants.ToDictionary(p => p.ParticipantId);

        Assert.Equal(1, scores[a.ParticipantId].LeaguePosition);
        Assert.Equal(1, scores[b.ParticipantId].LeaguePosition);
        Assert.Equal(50, scores[a.ParticipantId].LeaguePoints);
        Assert.Equal(50, scores[b.ParticipantId].LeaguePoints);
        Assert.Equal(3, scores[c.ParticipantId].LeaguePosition); // position 2 skipped
        Assert.Equal(48, scores[c.ParticipantId].LeaguePoints); // 50 - (3-1)*1
    }

    [Fact]
    public void Zero_penalties_means_adjusted_equals_raw()
    {
        var participant = new ParticipantInput(Guid.NewGuid(), null, [new RunInput(1, 123_456, 0, false)]);

        var score = EventScorer.Score([participant], Rules, new Dictionary<Guid, LeagueRules>()).Participants.Single();

        Assert.Equal(123_456, score.EventTimeMs);
    }

    [Fact]
    public void Twenty_penalties_add_twenty_times_the_penalty_seconds()
    {
        var participant = new ParticipantInput(Guid.NewGuid(), null, [new RunInput(1, 100_000, 20, false)]);

        var score = EventScorer.Score([participant], Rules, new Dictionary<Guid, LeagueRules>()).Participants.Single();

        // 100_000 + 20 * 5.00s * 1000 = 100_000 + 100_000
        Assert.Equal(200_000, score.EventTimeMs);
    }

    [Fact]
    public void Two_leagues_in_one_event_are_scored_independently_of_each_other_and_of_the_overall_table()
    {
        var leagueA = Guid.NewGuid();
        var leagueB = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules>
        {
            [leagueA] = new(50, 1, 0, true),
            [leagueB] = new(20, 2, 0, true), // a deliberately different points table
        };

        // Times interleaved across divisions so overall order doesn't match either
        // league's internal order.
        var a1 = new ParticipantInput(Guid.NewGuid(), leagueA, [new RunInput(1, 90_000, 0, false)]);
        var a2 = new ParticipantInput(Guid.NewGuid(), leagueA, [new RunInput(1, 95_000, 0, false)]);
        var b1 = new ParticipantInput(Guid.NewGuid(), leagueB, [new RunInput(1, 92_000, 0, false)]);
        var b2 = new ParticipantInput(Guid.NewGuid(), leagueB, [new RunInput(1, 98_000, 0, false)]);

        var scores = EventScorer.Score([a1, a2, b1, b2], Rules, leagueRules)
            .Participants.ToDictionary(p => p.ParticipantId);

        Assert.Equal(1, scores[a1.ParticipantId].LeaguePosition);
        Assert.Equal(50, scores[a1.ParticipantId].LeaguePoints);
        Assert.Equal(2, scores[a2.ParticipantId].LeaguePosition);
        Assert.Equal(49, scores[a2.ParticipantId].LeaguePoints);

        Assert.Equal(1, scores[b1.ParticipantId].LeaguePosition);
        Assert.Equal(20, scores[b1.ParticipantId].LeaguePoints);
        Assert.Equal(2, scores[b2.ParticipantId].LeaguePosition);
        Assert.Equal(18, scores[b2.ParticipantId].LeaguePoints); // 20 - 1*2

        // Overall spans both leagues, ordered purely by time: a1 < b1 < a2 < b2.
        Assert.Equal(1, scores[a1.ParticipantId].OverallPosition);
        Assert.Equal(2, scores[b1.ParticipantId].OverallPosition);
        Assert.Equal(3, scores[a2.ParticipantId].OverallPosition);
        Assert.Equal(4, scores[b2.ParticipantId].OverallPosition);
    }

    [Fact]
    public void Invalid_input_is_rejected_rather_than_silently_misscored()
    {
        var notDnfButNoTime = new ParticipantInput(Guid.NewGuid(), null, [new RunInput(1, null, 0, false)]);
        Assert.Throws<ArgumentException>(() =>
            EventScorer.Score([notDnfButNoTime], Rules, new Dictionary<Guid, LeagueRules>()));

        var negativePenalty = new ParticipantInput(Guid.NewGuid(), null, [new RunInput(1, 100_000, -1, false)]);
        Assert.Throws<ArgumentException>(() =>
            EventScorer.Score([negativePenalty], Rules, new Dictionary<Guid, LeagueRules>()));

        var leagueId = Guid.NewGuid();
        var missingRules = new ParticipantInput(Guid.NewGuid(), leagueId, [new RunInput(1, 100_000, 0, false)]);
        Assert.Throws<ArgumentException>(() =>
            EventScorer.Score([missingRules], Rules, new Dictionary<Guid, LeagueRules>()));
    }

    /// <summary>
    /// A synthetic, hand-verified scenario exercising every rule at once in a single
    /// event — including the tie-break and DNF cases the real fixture in
    /// <see cref="RealEventGoldenFixtureTests"/> happens not to contain. See the worked
    /// positions/points below for how each value was derived.
    /// </summary>
    [Fact]
    public void Synthetic_regression_a_five_shooter_night_with_a_tie_a_partial_dnf_and_a_full_dnf()
    {
        var leagueId = Guid.NewGuid();
        var leagueRules = new Dictionary<Guid, LeagueRules> { [leagueId] = new(50, 1, 0, true) };

        // s3 and s1 both post a best time of 90_000ms; s3's other run (93_000) beats
        // s1's (95_000), so s3 wins the tie outright — position 3 is not shared.
        var s1 = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 90_000, 0, false), new RunInput(2, 95_000, 0, false)]);
        var s2 = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 91_000, 0, false), new RunInput(2, null, 0, true)]);
        var s3 = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 90_000, 0, false), new RunInput(2, 93_000, 0, false)]);
        var s4 = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, null, 0, true), new RunInput(2, null, 0, true)]);
        var s5 = new ParticipantInput(Guid.NewGuid(), leagueId,
            [new RunInput(1, 100_000, 2, false), new RunInput(2, null, 0, true)]); // adjusted: 100_000 + 2*5_000 = 110_000

        var scores = EventScorer.Score([s1, s2, s3, s4, s5], Rules, leagueRules)
            .Participants.ToDictionary(p => p.ParticipantId);

        // Order: s3 (90_000/93_000) < s1 (90_000/95_000) < s2 (91_000) < s5 (110_000); s4 DNF.
        AssertRanked(scores[s3.ParticipantId], eventTimeMs: 90_000, bestRun: 1, position: 1, points: 50);
        AssertRanked(scores[s1.ParticipantId], eventTimeMs: 90_000, bestRun: 1, position: 2, points: 49);
        AssertRanked(scores[s2.ParticipantId], eventTimeMs: 91_000, bestRun: 1, position: 3, points: 48);
        AssertRanked(scores[s5.ParticipantId], eventTimeMs: 110_000, bestRun: 1, position: 4, points: 47);

        var dnf = scores[s4.ParticipantId];
        Assert.Equal(ParticipantScoringStatus.Dnf, dnf.Status);
        Assert.Null(dnf.OverallPosition);
        Assert.Null(dnf.LeaguePosition);
        Assert.Equal(0, dnf.LeaguePoints);

        static void AssertRanked(ParticipantScore score, int eventTimeMs, int bestRun, int position, int points)
        {
            Assert.Equal(ParticipantScoringStatus.Ranked, score.Status);
            Assert.Equal(eventTimeMs, score.EventTimeMs);
            Assert.Equal(bestRun, score.BestRunNumber);
            Assert.Equal(position, score.OverallPosition);
            Assert.Equal(position, score.LeaguePosition);
            Assert.Equal(points, score.LeaguePoints);
        }
    }
}
